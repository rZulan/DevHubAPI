using System.Text.Json;
using DevHub.Application.Common;
using DevHub.Application.Connections;
using DevHub.Application.Notifications;
using DevHub.Application.ProjectTools;
using DevHub.Application.Todos;
using DevHub.Domain.Projects;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

internal static class ConnectionChecks
{
    public static async Task Run(IServiceProvider provider)
    {
        await using var fixture = provider.CreateAsyncScope();
        var db = fixture.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = await db.Projects.AsNoTracking().FirstAsync();
        var ownerId = await db.Organizations.Where(organization => organization.Id == source.OrganizationId)
            .Select(organization => organization.OwnerUserId).SingleAsync();
        var project = Project.Create(source.OrganizationId, source.TeamId, ownerId, "Project Connections checks", "Automation checks",
            ProjectStatus.Planning, [], null, null, null, null, null, null, [], null, null, DateTimeOffset.UtcNow);
        db.Add(project);
        await db.SaveChangesAsync();

        var scope = new ConnectionScope(project.OrganizationId, project.Id, ownerId);
        await SdlcConnectionChecks.Run(fixture.ServiceProvider, scope, default);
        await ConnectionAdapterChecks.Run(fixture.ServiceProvider, scope, default);
        var todos = fixture.ServiceProvider.GetRequiredService<ITodoService>();
        var tools = fixture.ServiceProvider.GetRequiredService<IProjectToolsService>();
        var connections = fixture.ServiceProvider.GetRequiredService<IProjectConnections>();
        var board = Require(await todos.Create(scope.OrganizationId, scope.ProjectId, ownerId, new("Schema work", "", null), default));
        var task = Require(await todos.AddTask(scope.OrganizationId, scope.ProjectId, board.Id, ownerId,
            new("Create Users table", "", "progress"), default));
        var erd = new ConnectionNodeRef("erd");
        var taskNode = new ConnectionNodeRef("todo-task", task.Id.ToString(), board.Id.ToString());
        var calendar = new ConnectionNodeRef("calendar");

        var previous = Require(await SaveErd(0, "table_1"));
        var milestoneStep = Guid.NewGuid();
        var whenUsers = new ConnectionRule(Guid.NewGuid(), "Complete the task when Users appears", true, false, erd, "table-name-appears",
            Args("name", "Users"), false, [new(taskNode, "status-is", Args("status", "progress"))],
            [new(Guid.NewGuid(), taskNode, "move-task", Args("status", "done")),
                new(milestoneStep, calendar, "create-entry", new Dictionary<string, string>
                {
                    ["title"] = "Review Users schema", ["date"] = DateTime.UtcNow.AddDays(7).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), ["kind"] = "milestone"
                }),
                new(Guid.NewGuid(), new("calendar-entry"), "set-title", new Dictionary<string, string>(), milestoneStep,
                    new Dictionary<string, ConnectionArgumentBinding> { ["title"] = new("name") })]);
        var reverse = new ConnectionRule(Guid.NewGuid(), "Create an Audit table when the task starts", true, false, taskNode, "status-changed",
            Args("status", "progress"), false, [new(erd, "table-missing", Args("name", "Audit"))],
            [new(Guid.NewGuid(), erd, "create-table", Args("name", "Audit"))]);

        var draft = new SaveConnectionRequest("Schema workflow", 0, [erd, taskNode, calendar], [whenUsers, reverse]);
        var preview = Require(await connections.Validate(scope, null, draft, default));
        Check(preview.Rules.All(rule => rule.Status == "ready" && rule.Conditions.All(condition => condition.Passed == true)) &&
            Require(await connections.List(scope, default)).Count == 0, "The draft check evaluates current guards without storing a connection");
        var definition = Require(await connections.Save(scope, null, draft, default));
        Check(!((await connections.Get(scope with { OrganizationId = Guid.NewGuid() }, definition.Id, default)).IsSuccess),
            "A Connections definition cannot be read through another organization");
        var renamed = Require(await SaveErd(previous.Revision, "Users"));
        Check((await SaveErd(previous.Revision, "Users")).Error?.Type == ErrorType.Conflict,
            "A stale ERD save cannot publish a matching event");
        var updated = whenUsers with { Title = "Finish schema task", Actions = whenUsers.Actions.Take(2).ToArray() };
        Require(await connections.Save(scope, definition.Id, draft with { Revision = definition.Revision, Rules = [updated, reverse] }, default));

        var worker = provider.GetServices<IHostedService>().Single(service => service.GetType().Name == "ConnectionWorker");
        await worker.StartAsync(default);
        try
        {
            var runs = await WaitForRuns(1);
            Check(runs.Count == 1 && runs[0].Status == "succeeded" && runs[0].RuleTitle == whenUsers.Title &&
                runs[0].Steps.Count == 3 && runs[0].Steps.All(step => step.Status == "succeeded"),
                "ERD rename commits one Connections run with the rule revision active at the event", JsonSerializer.Serialize(runs));
            var finished = Require(await todos.Get(scope.OrganizationId, scope.ProjectId, board.Id, ownerId, default));
            Check(finished.Tasks.Single().Status == "done" && finished.Tasks.Single().FinishedAtUtc is not null && finished.FinishedAtUtc is not null,
                "Automation moves the task using native task and board completion behavior");
            var schedule = Require(await tools.Get(scope.OrganizationId, scope.ProjectId, ownerId, "calendar", default));
            Check(schedule.Document?.GetProperty("events").EnumerateArray().Single().GetProperty("title").GetString() == "Users",
                "The rule creates one milestone and sets the new item’s title from the triggering event field");

            db.ChangeTracker.Clear();
            Require(await todos.MoveTask(scope.OrganizationId, scope.ProjectId, board.Id, task.Id, ownerId, new("pending"), default));
            var removedName = Require(await SaveErd(renamed.Revision, "table_1"));
            Require(await SaveErd(removedName.Revision, "Users"));
            var second = await WaitForRuns(2);
            Check(second.Count == 2 && second.Count(run => run.Status == "skipped") == 1,
                "An intervening manual status change prevents the next automated task move");
            var remained = Require(await todos.Get(scope.OrganizationId, scope.ProjectId, board.Id, ownerId, default));
            Check(remained.Tasks.Single().Status == "pending" && remained.FinishedAtUtc is null,
                "A skipped rule preserves the manual status change");

            Require(await todos.MoveTask(scope.OrganizationId, scope.ProjectId, board.Id, task.Id, ownerId, new("progress"), default));
            var reverseRuns = await WaitForRuns(3);
            var finalSchema = Require(await tools.Get(scope.OrganizationId, scope.ProjectId, ownerId, "erd", default));
            Check(reverseRuns.Count == 3 && reverseRuns.Single(run => run.RuleId == reverse.Id).Status == "succeeded" &&
                finalSchema.Document?.GetProperty("tables").EnumerateArray().Any(table => table.GetProperty("name").GetString() == "Audit") == true,
                "The reverse rule creates an ERD table from a committed task transition", JsonSerializer.Serialize(reverseRuns));
            var renamedAway = Require(await SaveErd(finalSchema.Revision, "table_1"));
            var usersAgain = Require(await SaveErd(renamedAway.Revision, "Users"));
            var repeatedRuns = await WaitForRuns(4);
            var notifications = fixture.ServiceProvider.GetRequiredService<INotificationService>();
            var recurringNotices = Require(await notifications.List(scope.OrganizationId, ownerId, null, true, default)).Items
                .Where(item => item.Kind == "connection-triggered" && item.ContextId == definition.Id).ToArray();
            Check(repeatedRuns.Count(run => run.RuleId == whenUsers.Id && run.Status == "succeeded") == 2 &&
                recurringNotices.Length == 3 && recurringNotices.All(item => item.ActorName == "Automation" &&
                    repeatedRuns.Any(run => run.Id == item.TargetId && run.Status == "succeeded")),
                "A recurring rule sends a notification for each trigger, excluding events that fail its conditions");

            var once = new ConnectionRule(Guid.NewGuid(), "Name the invoice task once", true, false, erd, "table-name-appears",
                Args("name", "Invoices"), false, [new(taskNode, "status-is", Args("status", "pending"))],
                [new(Guid.NewGuid(), taskNode, "set-title", Args("title", "Invoice table task"))], RunOnce: true);
            var onceDraft = new SaveConnectionRequest("Invoice workflow", 0, [erd, taskNode], [once]);
            var onceDefinition = Require(await connections.Save(scope, null, onceDraft, default));
            var invoiceSchema = Require(await SaveErd(usersAgain.Revision, "Invoices"));
            await WaitForQueue();
            Check(Require(await connections.Get(scope, onceDefinition.Id, default)).TriggeredRuleIds!.Count == 0,
                "A failed condition does not consume a run-once rule");

            db.ChangeTracker.Clear();
            Require(await todos.MoveTask(scope.OrganizationId, scope.ProjectId, board.Id, task.Id, ownerId, new("pending"), default));
            invoiceSchema = Require(await SaveErd(invoiceSchema.Revision, "table_1"));
            invoiceSchema = Require(await SaveErd(invoiceSchema.Revision, "Invoices"));
            invoiceSchema = Require(await SaveErd(invoiceSchema.Revision, "table_1"));
            invoiceSchema = Require(await SaveErd(invoiceSchema.Revision, "Invoices"));
            await WaitForQueue();
            var triggered = Require(await connections.Get(scope, onceDefinition.Id, default));
            var onceRuns = Require(await connections.Runs(scope, onceDefinition.Id, 1, default));
            var onceNotices = Require(await notifications.List(scope.OrganizationId, ownerId, null, true, default)).Items
                .Where(item => item.Kind == "connection-triggered" && item.ContextId == onceDefinition.Id).ToArray();
            Check(triggered.TriggeredRuleIds!.SequenceEqual(new[] { once.Id }) && onceRuns.Count(run => run.Status == "succeeded") == 1 &&
                onceNotices.Length == 1 && onceNotices[0].TargetId == onceRuns.Single(run => run.Status == "succeeded").Id,
                "Repeated matches yield one admitted run and one notification for a run-once rule");

            db.ChangeTracker.Clear();
            var paused = Require(await connections.Save(scope, onceDefinition.Id, onceDraft with
                { Revision = triggered.Revision, Rules = [once with { Enabled = false }] }, default));
            Require(await connections.Save(scope, onceDefinition.Id, onceDraft with { Revision = paused.Revision }, default));
            invoiceSchema = Require(await SaveErd(invoiceSchema.Revision, "table_1"));
            Require(await SaveErd(invoiceSchema.Revision, "Invoices"));
            await WaitForQueue();
            Check(Require(await connections.Runs(scope, onceDefinition.Id, 1, default)).Count(run => run.Status == "succeeded") == 2,
                "Pausing and re-enabling a run-once rule arms it for one new trigger");
            await CalendarConnectionChecks.Run(fixture.ServiceProvider, scope, taskNode, default);
        }
        finally
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StopAsync(timeout.Token);
        }

        async Task<IReadOnlyList<ConnectionRunResponse>> WaitForRuns(int count)
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (true)
            {
                var runs = Require(await connections.Runs(scope, definition.Id, 1, deadline.Token));
                if (runs.Count >= count && runs.All(run => run.Status != "pending")) return runs;
                await Task.Delay(200, deadline.Token);
            }
        }

        async Task WaitForQueue()
        {
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            while (await db.Database.SqlQuery<int>($"""
                SELECT COUNT(*) AS [Value] FROM [ProjectConnectionEvents]
                WHERE [ProjectId] = {scope.ProjectId} AND [FinishedAtUtc] IS NULL
                """).SingleAsync(deadline.Token) > 0)
                await Task.Delay(200, deadline.Token);
        }

        Task<Result<ToolDocumentResponse>> SaveErd(int revision, string name) => tools.Save(scope.OrganizationId, scope.ProjectId, ownerId, "erd",
            new(revision, JsonSerializer.Deserialize<JsonElement>($$"""
                {"tables":[{"id":"users","name":"{{name}}","x":10,"y":20,"fields":[{"id":"user-id","name":"id","type":"UUID","length":"","primary":true,"nullable":false,"unique":false,"defaultValue":""}]}],"relationships":[]}
                """)), default);
    }

    private static Dictionary<string, string> Args(string key, string value) => new() { [key] = value };
    private static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new Exception($"{result.Error!.Code}: {result.Error.Description}");
    private static void Check(bool condition, string title, string? detail = null)
    {
        if (!condition) throw new Exception(detail is null ? title : $"{title}. {detail}");
        Console.WriteLine("PASS: " + title);
    }
}
