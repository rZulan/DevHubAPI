using System.Text.Json;
using System.Text.Json.Nodes;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Connections;
using DevHub.Application.ProjectTools;
using DevHub.Application.Todos;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class SdlcConnectionChecks
{
    public static async Task Run(IServiceProvider provider, ConnectionScope scope, CancellationToken ct)
    {
        var tools = provider.GetRequiredService<IConnectionTools>();
        var documents = provider.GetRequiredService<IProjectToolsService>();
        var todos = provider.GetRequiredService<ITodoService>();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var roadmap = new ConnectionNodeRef("sdlc");
        var requirements = new ConnectionNodeRef("sdlc-phase", "1:requirements", "waterfall");
        var draft = JsonSerializer.Deserialize<JsonElement>("""{"model":"waterfall","currentPhase":0,"completed":false,"notes":{}}""");
        if (Require(await Load()).Document is not null ||
            (await documents.Save(scope.OrganizationId, scope.ProjectId, scope.UserId, "sdlc", new(0, draft), ct)).IsSuccess)
            throw new InvalidOperationException("A project could edit a roadmap before initializing it.");

        var legacy = """{"model":"waterfall","currentPhase":0,"completed":false,"notes":{"requirements":"Existing notes"},"modelStates":{"v-model":{"currentPhase":1,"completed":false,"notes":{}}}}""";
        await db.Database.ExecuteSqlAsync($"""
            INSERT INTO [ProjectDocuments] ([Id], [ProjectId], [ToolType], [ContentJson], [Revision])
            VALUES ({Guid.NewGuid()}, {scope.ProjectId}, N'sdlc', {legacy}, 1)
            """, ct);
        var savedLegacy = Require(await documents.Save(scope.OrganizationId, scope.ProjectId, scope.UserId, "sdlc", new(1, draft), ct));
        if (!savedLegacy.Document!.Value.TryGetProperty("modelStates", out _) || Require(await Boards()).Count != 0 ||
            await tools.ResolveNode(scope, new("sdlc-model", "v-model"), ct) is not null)
            throw new InvalidOperationException("A legacy roadmap lost inactive progress or exposed it as a current model.");

        var created = Require(await documents.InitializeSdlc(scope.OrganizationId, scope.ProjectId, scope.UserId, new(savedLegacy.Revision, "waterfall"), ct));
        var initialized = created.Document!.Value;
        if (initialized.GetProperty("creatorId").GetGuid() != scope.UserId || !initialized.GetProperty("connectToTodo").GetBoolean())
            throw new InvalidOperationException("Initialization did not record the creator and the default To-do connection.");
        if ((await documents.InitializeSdlc(scope.OrganizationId, scope.ProjectId, scope.UserId, new(0, "waterfall"), ct)).Error?.Type != ErrorType.Conflict)
            throw new InvalidOperationException("A stale setup could replace the existing roadmap.");
        var originalBoards = Require(await Boards());
        if (originalBoards.Count != 6 || originalBoards.Any(board => board.Tasks.Count == 0))
            throw new InvalidOperationException("The connected roadmap did not create one board with deliverables per phase.");
        var requirementsBoard = originalBoards.Single(board => board.Name == "Requirements");
        Require(await todos.ConfigureColumns(scope.OrganizationId, scope.ProjectId, requirementsBoard.Id, scope.UserId,
            new([new("pending", "Pending"), new("progress", "In progress"), new("accepted", "Accepted")], requirementsBoard.BoardRevision), ct));

        if (await tools.ResolveNode(scope, requirements, ct) is not { Label: "Requirements" })
            throw new InvalidOperationException("The SDLC phase picker did not resolve the saved Waterfall phase.");
        if ((await Apply("advance", requirements)).IsSuccess)
            throw new InvalidOperationException("A roadmap action advanced before the required checklist was complete.");
        Require(await Apply("check-deliverable", requirements, "item", "Requirements specification"));
        if (Require(await Boards()).Single(board => board.Id == requirementsBoard.Id).Tasks
            .Single(task => task.Title == "Requirements specification").Status != "accepted")
            throw new InvalidOperationException("Checking an SDLC deliverable did not move its task to the current last column.");
        Require(await Apply("uncheck-deliverable", requirements, "item", "Requirements specification"));
        if (Require(await Boards()).Single(board => board.Id == requirementsBoard.Id).Tasks
            .Single(task => task.Title == "Requirements specification").Status != "pending")
            throw new InvalidOperationException("Unchecking an SDLC deliverable did not reopen its task.");
        Require(await Apply("check-deliverable", requirements, "item", "Requirements specification"));
        Require(await Apply("check-deliverable", requirements, "item", "stakeholder approval"));
        Require(await Apply("block", requirements, "blocker", "Review pending"));
        if ((await Apply("advance", requirements)).IsSuccess)
            throw new InvalidOperationException("A roadmap action advanced despite a blocker.");
        Require(await Apply("unblock", requirements));
        Require(await Apply("advance", requirements));
        Require(await Apply("set-phase-note", requirements, "note", "Reviewed in automation smoke test"));
        if ((await Apply("set-active-model", roadmap, "model", "agile")).IsSuccess)
            throw new InvalidOperationException("A roadmap connection could switch the initialized model in place.");

        var response = Require(await Load());
        var saved = response.Document!.Value;
        if (saved.GetProperty("currentPhase").GetInt32() != 1 || saved.GetProperty("notes").GetProperty("requirements").GetString() != "Reviewed in automation smoke test")
            throw new InvalidOperationException("Automation did not preserve phase progress and notes.");
        var otherModel = JsonSerializer.Deserialize<JsonElement>("""{"model":"agile","currentPhase":0,"completed":false,"notes":{}}""");
        if ((await documents.Save(scope.OrganizationId, scope.ProjectId, scope.UserId, "sdlc", new(response.Revision, otherModel), ct)).IsSuccess)
            throw new InvalidOperationException("An ordinary roadmap save switched the initialized model.");

        var organization = (await provider.GetRequiredService<IOrganizationRepository>().GetByIdAsync(scope.OrganizationId, ct))!;
        var teamId = await db.Projects.Where(project => project.Id == scope.ProjectId).Select(project => project.TeamId).SingleAsync(ct);
        var now = DateTimeOffset.UtcNow;
        var editor = User.Create("roadmap-editor@example.test", "roadmap_editor", "Roadmap", "Editor", "test-only-hash", now);
        db.Users.Add(editor);
        organization.AddMember(editor.Id, now);
        var role = organization.AddRole("Roadmap editor", "#38bdf8", [OrganizationPermissions.ManageRoadmap], now);
        organization.AssignRole(editor.Id, role.Id, now);
        organization.Teams.Single(team => team.Id == teamId).AddMember(editor.Id, now);
        await db.SaveChangesAsync(ct);
        if ((await documents.InitializeSdlc(scope.OrganizationId, scope.ProjectId, editor.Id, new(response.Revision, "agile"), ct)).Error?.Type != ErrorType.Forbidden)
            throw new InvalidOperationException("A user without Manage tasks could enable the To-do connection.");

        var customized = JsonNode.Parse(saved.GetRawText())!.AsObject();
        customized["work"]!["design"] = new JsonObject { ["deliverables"] = new JsonArray("Design brief") };
        if ((await Save(editor.Id, response.Revision)).Error?.Type != ErrorType.Forbidden)
            throw new InvalidOperationException("A roadmap editor other than the creator could change its deliverable list.");
        var custom = Require(await Save(scope.UserId, response.Revision));
        customized["creatorId"] = editor.Id.ToString();
        customized["connectToTodo"] = false;
        customized["roadmapId"] = Guid.NewGuid().ToString();
        customized["work"]!["design"]!["assigneeIds"] = new JsonArray(scope.UserId.ToString(), editor.Id.ToString());
        customized["work"]!["design"]!["targetDate"] = "2026-12-03";
        customized["work"]!["design"]!["deliverableTargetDates"] = new JsonObject { ["Design brief"] = "2026-12-01" };
        var assigned = Require(await Save(editor.Id, custom.Revision));
        if (assigned.Document!.Value.GetProperty("creatorId").GetGuid() != scope.UserId ||
            assigned.Document.Value.GetProperty("roadmapId").GetGuid() != initialized.GetProperty("roadmapId").GetGuid() ||
            !assigned.Document.Value.GetProperty("connectToTodo").GetBoolean())
            throw new InvalidOperationException("An editor could change the roadmap setup through an ordinary save.");
        var designBoard = Require(await Boards()).Single(board => board.Name == "System design");
        if (designBoard.TargetDate != new DateOnly(2026, 12, 3) ||
            designBoard.Tasks.Single(task => task.Title == "Design brief").TargetDate != new DateOnly(2026, 12, 1))
            throw new InvalidOperationException("Phase and deliverable target dates did not sync to To-do.");

        var design = new ConnectionNodeRef("sdlc-phase", "1:design", "waterfall");
        if (!Require(await tools.Check(scope, new(design, "phase-owner-is", new Dictionary<string, string> { ["ownerId"] = editor.Id.ToString() }), ct)))
            throw new InvalidOperationException("The assignee condition did not recognize a second phase assignee.");
        if ((await Apply("advance", design)).IsSuccess || (await Apply("check-deliverable", design, "item", "Architecture")).IsSuccess)
            throw new InvalidOperationException("Connections ignored the custom phase checklist.");
        Require(await Apply("check-deliverable", design, "item", "Design brief"));
        Require(await Apply("advance", design));
        var requirementsAfter = Require(await Boards()).Single(board => board.Id == requirementsBoard.Id);
        if (requirementsAfter.Columns[^1].Id != "accepted" || !requirementsAfter.Tasks.All(task => task.Status == "accepted"))
            throw new InvalidOperationException("Unrelated roadmap edits changed the custom To-do columns or deliverable states.");
        var latest = Require(await Load());
        var reopenEvents = await PhaseReopenEvents();
        var disconnected = Require(await documents.InitializeSdlc(scope.OrganizationId, scope.ProjectId, scope.UserId,
            new(latest.Revision, "waterfall", false), ct));
        var reset = disconnected.Document!.Value;
        if (reset.GetProperty("model").GetString() != "waterfall" || reset.GetProperty("currentPhase").GetInt32() != 0 ||
            reset.GetProperty("notes").EnumerateObject().Any() || reset.TryGetProperty("work", out _) ||
            reset.GetProperty("roadmapId").GetGuid() == initialized.GetProperty("roadmapId").GetGuid() ||
            !Require(await Boards()).Select(board => board.Id).ToHashSet().SetEquals(originalBoards.Select(board => board.Id)))
            throw new InvalidOperationException("Starting a disconnected roadmap did not reset the plan and preserve existing To-do boards.");
        if (reopenEvents != await PhaseReopenEvents())
            throw new InvalidOperationException("Resetting the same model treated its new phases as reopened work.");
        Require(await documents.InitializeSdlc(scope.OrganizationId, scope.ProjectId, scope.UserId, new(disconnected.Revision, "iterative"), ct));
        var replacementBoards = Require(await Boards()).Where(board => originalBoards.All(previous => previous.Id != board.Id)).ToArray();
        if (replacementBoards.Length != 6 || !replacementBoards.Single(board => board.Name == "Analyze").Tasks
            .Select(task => task.Title).SequenceEqual(["Refined requirements", "risk assessment"]))
            throw new InvalidOperationException("A connected reset did not create distinct boards for the replacement model.");
        Console.WriteLine("SDLC initialization, connected To-do boards, custom completion columns, dates, permissions and reset checks passed.");

        Task<Result<ToolDocumentResponse>> Load() => documents.Get(scope.OrganizationId, scope.ProjectId, scope.UserId, "sdlc", ct);
        Task<Result<IReadOnlyList<TodoResponse>>> Boards() => todos.List(scope.OrganizationId, scope.ProjectId, scope.UserId, ct);
        Task<int> PhaseReopenEvents() => db.Database.SqlQuery<int>($"""
            SELECT COUNT(*) AS [Value] FROM [ProjectConnectionEvents]
            WHERE [ProjectId] = {scope.ProjectId} AND JSON_VALUE([DraftJson], '$.kind') = 'phase-reopened'
            """).SingleAsync(ct);

        Task<Result<ToolDocumentResponse>> Save(Guid editorId, int revision) =>
            documents.Save(scope.OrganizationId, scope.ProjectId, editorId, "sdlc", new(revision, JsonSerializer.SerializeToElement(customized)), ct);

        Task<Result<ConnectionActionResult>> Apply(string action, ConnectionNodeRef target, string? key = null, string? value = null) =>
            tools.Execute(scope, new(Guid.NewGuid(), target, action, key is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [key] = value! }), ct);
    }

    private static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new InvalidOperationException(result.Error?.Description);
}
