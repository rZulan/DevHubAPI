using System.Reflection;
using DevHub.Application.Common;
using DevHub.Application.Todos;
using DevHub.Domain.Todos;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Run from api/: dotnet run --project tests/DevHub.TodoSmoke -c Release
// All test writes are rolled back, including on assertion failure.
var configuration = new ConfigurationBuilder()
    .SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true)
    .AddEnvironmentVariables().Build();
var services = new ServiceCollection();
await using var isolatedDatabase = args.Contains("--isolated") ? new IsolatedSmokeDatabase(configuration) : null;
services.AddLogging();
services.AddInfrastructure(configuration);
// Rollback-based checks use one explicit transaction, without retrying it piecemeal.
services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"), sql => sql.ExecutionStrategy(dependencies => new Microsoft.EntityFrameworkCore.Storage.NonRetryingExecutionStrategy(dependencies))));
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var service = scope.ServiceProvider.GetRequiredService<ITodoService>();
if (isolatedDatabase is not null) await isolatedDatabase.Seed(db, "20261027161918_AddProjectCalendar", async (context, _) => await context.Database.MigrateAsync());
await using var transaction = await db.Database.BeginTransactionAsync();
try
{
    var project = await db.Projects.AsNoTracking().FirstAsync();
    var owner = await db.Organizations.Where(o => o.Id == project.OrganizationId).Select(o => o.OwnerUserId).SingleAsync();
    var org = project.OrganizationId;
    var ct = CancellationToken.None;
    var target = new DateOnly(2026, 12, 31);
    var start = new DateOnly(2026, 12, 21);
    var first = Require(await service.Create(org, project.Id, owner, new("Persistence smoke A", "Context", target, start), ct));
    var second = Require(await service.Create(org, project.Id, owner, new("Persistence smoke B", null, null), ct));
    var task = Require(await service.AddTask(org, project.Id, first.Id, owner, new("Saved task", "Task context", "pending"), ct));
    Require(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("progress"), ct));
    db.ChangeTracker.Clear();
    var reloaded = Require(await service.List(org, project.Id, owner, ct));
    Check(reloaded.Single(t => t.Id == first.Id).TargetDate == target, "Target date persists");
    Check(reloaded.Single(t => t.Id == first.Id).StartDate == start, "Optional TODO From date persists");
    Check(!(await service.Create(org, project.Id, owner, new("Invalid range", null, start, target), ct)).IsSuccess, "Reversed TODO dates rejected at creation");
    Check(!(await service.Create(org, project.Id, owner, new("Missing target", null, null, start), ct)).IsSuccess, "From without a TODO target rejected");
    var datesBoard = Require(await service.Get(org, project.Id, first.Id, owner, ct));
    var datesRequest = new UpdateTodoDatesRequest(target.AddDays(2), start.AddDays(1), datesBoard.BoardRevision);
    Check(!(await service.UpdateDates(org, Guid.NewGuid(), first.Id, owner, datesRequest, ct)).IsSuccess, "Cross-project TODO date update rejected");
    Check(!(await service.UpdateDates(Guid.NewGuid(), project.Id, first.Id, owner, datesRequest, ct)).IsSuccess, "Cross-organization TODO date update rejected");
    Check(!(await service.UpdateDates(org, project.Id, first.Id, Guid.NewGuid(), datesRequest, ct)).IsSuccess, "Non-member TODO date update rejected");
    Check(!(await service.UpdateDates(org, project.Id, first.Id, owner, datesRequest with {StartDate = target.AddDays(3)}, ct)).IsSuccess, "Reversed TODO date update rejected");
    var dated = Require(await service.UpdateDates(org, project.Id, first.Id, owner, datesRequest, ct));
    db.ChangeTracker.Clear();
    var datedReload = Require(await service.Get(org, project.Id, first.Id, owner, ct));
    Check(datedReload.StartDate == datesRequest.StartDate && datedReload.TargetDate == datesRequest.TargetDate, "Edited TODO dates persist after reload");
    Check(datedReload.BoardRevision == datesBoard.BoardRevision + 1, "TODO date edit advances revision");
    Check(!(await service.UpdateDates(org, project.Id, first.Id, owner, datesRequest, ct)).IsSuccess, "Stale TODO date update rejected");
    dated = Require(await service.UpdateDates(org, project.Id, first.Id, owner, new(target, null, dated.BoardRevision), ct));
    Check(dated.StartDate is null && dated.TargetDate == target, "From date can be removed independently");
    dated = Require(await service.UpdateDates(org, project.Id, first.Id, owner, new(null, null, dated.BoardRevision), ct));
    Check(dated.StartDate is null && dated.TargetDate is null, "TODO dates can be cleared");
    var scheduledTask = Require(await service.AddTask(org, project.Id, second.Id, owner, new("Scheduled task", "", "pending", TargetDate: target, StartDate: start), ct));
    db.ChangeTracker.Clear();
    var scheduledReload = Require(await service.Get(org, project.Id, second.Id, owner, ct)).Tasks.Single();
    Check(scheduledReload.StartDate == start && scheduledReload.TargetDate == target, "Task date range persists after reload");
    Check(!(await service.AddTask(org, project.Id, second.Id, owner, new("Invalid task", "", "pending", TargetDate: start, StartDate: target), ct)).IsSuccess, "Reversed task dates rejected");
    Check(!(await service.AddTask(org, project.Id, second.Id, owner, new("Invalid task", "", "pending", StartDate: start), ct)).IsSuccess, "Task From requires a target date");
    var scheduledBoard = Require(await service.Get(org, project.Id, second.Id, owner, ct));
    var taskDatesEdit = new UpdateTodoTaskRequest("Scheduled task", "", "pending", [], scheduledBoard.BoardRevision, target.AddDays(2), start.AddDays(1));
    Check(!(await service.UpdateTask(org, project.Id, second.Id, scheduledTask.Id, owner, taskDatesEdit with {StartDate = target.AddDays(3)}, ct)).IsSuccess, "Reversed task date edit rejected");
    Require(await service.UpdateTask(org, project.Id, second.Id, scheduledTask.Id, owner, taskDatesEdit, ct));
    Check(!(await service.UpdateTask(org, project.Id, second.Id, scheduledTask.Id, owner, taskDatesEdit, ct)).IsSuccess, "Stale task date edit rejected");
    db.ChangeTracker.Clear();
    scheduledBoard = Require(await service.Get(org, project.Id, second.Id, owner, ct));
    scheduledReload = scheduledBoard.Tasks.Single();
    Check(scheduledReload.StartDate == start.AddDays(1) && scheduledReload.TargetDate == target.AddDays(2), "Edited task dates persist after reload");
    var clearedTask = Require(await service.UpdateTask(org, project.Id, second.Id, scheduledTask.Id, owner, taskDatesEdit with {BoardRevision = scheduledBoard.BoardRevision, StartDate = null, TargetDate = null}, ct));
    Check(clearedTask.StartDate is null && clearedTask.TargetDate is null, "Task schedule can be cleared");
    scheduledBoard = Require(await service.Get(org, project.Id, second.Id, owner, ct));
    Require(await service.DeleteTask(org, project.Id, second.Id, scheduledTask.Id, owner, new(scheduledBoard.BoardRevision), ct));
    Check(reloaded.Single(t => t.Id == first.Id).Tasks.Single().Status == "progress", "Task move persists after reload");
    Check(reloaded.Single(t => t.Id == second.Id).Tasks.Count == 0, "TODO boards are isolated");
    Check(reloaded.Single(t => t.Id == second.Id).TargetDate is null, "Target date is optional");
    Check(Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single().Status == "progress", "Direct board lookup includes tasks");
    Check(!(await service.Get(org, Guid.NewGuid(), first.Id, owner, ct)).IsSuccess, "Cross-project board lookup rejected");
    for (var index = 0; index < 13; index++)
        Require(await service.Create(org, project.Id, owner, new($"Pagination smoke {index}", null, null), ct));
    var firstPage = Require(await service.ListPage(org, project.Id, owner, 0, ct));
    var secondPage = Require(await service.ListPage(org, project.Id, owner, 1, ct));
    Check(firstPage.Items.Count == 12 && firstPage.HasMore, "First page is bounded and indicates more TODOs");
    Check(secondPage.Items.Count > 0 && !firstPage.Items.Select(t => t.Id).Intersect(secondPage.Items.Select(t => t.Id)).Any(), "Next page contains distinct TODOs");
    var total = Require(await service.List(org, project.Id, owner, ct)).Count;
    var lastPage = Require(await service.ListPage(org, project.Id, owner, (total - 1) / 12, ct));
    Check(!lastPage.HasMore && lastPage.Items.Count == (total - 1) % 12 + 1, "Final page stops pagination");
    Check(!(await service.ListPage(org, project.Id, owner, -1, ct)).IsSuccess, "Negative page rejected");
    foreach (var size in new[] { 4, 8, 12 })
    {
        var responsiveFirst = Require(await service.ListPage(org, project.Id, owner, 0, ct, size));
        var responsiveNext = Require(await service.ListPage(org, project.Id, owner, 1, ct, size));
        Check(responsiveFirst.Items.Count == size && responsiveFirst.HasMore, $"Four rows at {size / 4} columns");
        Check(!responsiveFirst.Items.Select(t => t.Id).Intersect(responsiveNext.Items.Select(t => t.Id)).Any(), $"Distinct next page at size {size}");
    }
    Check(!(await service.ListPage(org, project.Id, owner, 0, ct, 0)).IsSuccess, "Zero page size rejected");
    Check(!(await service.ListPage(org, project.Id, owner, 0, ct, 101)).IsSuccess, "Excessive page size rejected");
    Check(!(await service.ListPage(org, project.Id, Guid.NewGuid(), 0, ct)).IsSuccess, "Non-member pagination rejected");
    Require(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("done"), ct));
    Check(!(await service.MoveTask(org, project.Id, second.Id, task.Id, owner, new("pending"), ct)).IsSuccess, "Cross-board task move rejected");
    Check(!(await service.List(org, project.Id, Guid.NewGuid(), ct)).IsSuccess, "Non-member access rejected");
    Check(!(await service.List(Guid.NewGuid(), project.Id, owner, ct)).IsSuccess, "Cross-organization access rejected");
    Check(!(await service.AddTask(org, Guid.NewGuid(), first.Id, owner, new("Invalid scope", null, "pending"), ct)).IsSuccess, "Cross-project write rejected");
    Check(!(await service.Create(org, project.Id, owner, new("   ", null, null), ct)).IsSuccess, "Whitespace TODO name rejected");
    Check(!(await service.AddTask(org, project.Id, first.Id, owner, new("   ", null, "pending"), ct)).IsSuccess, "Whitespace task title rejected");
    Check(!(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("invalid"), ct)).IsSuccess, "Invalid task status rejected");
    var assigned = Require(await service.AddTask(org, project.Id, first.Id, owner, new("Assigned task", null, "pending", owner), ct));
    db.ChangeTracker.Clear();
    Check(Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single(t => t.Id == assigned.Id).AssigneeId == owner, "Assignee persists at creation");
    Require(await service.AssignTask(org, project.Id, first.Id, task.Id, owner, new(owner), ct));
    Require(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("pending"), ct));
    db.ChangeTracker.Clear();
    Check(Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single(t => t.Id == task.Id).AssigneeId == owner, "Assignment persists across status changes");
    Check(!(await service.AssignTask(org, project.Id, first.Id, task.Id, owner, new(Guid.NewGuid()), ct)).IsSuccess, "Non-member assignee rejected");
    Check(!(await service.AddTask(org, project.Id, first.Id, owner, new("Invalid assignee", null, "pending", Guid.NewGuid()), ct)).IsSuccess, "Non-member assignee rejected at creation");
    Check(!(await service.AssignTask(org, project.Id, second.Id, task.Id, owner, new(owner), ct)).IsSuccess, "Cross-board assignment rejected");
    Check(!(await service.AssignTask(org, Guid.NewGuid(), first.Id, task.Id, owner, new(owner), ct)).IsSuccess, "Cross-project assignment rejected");
    Check(!(await service.AssignTask(org, project.Id, first.Id, task.Id, Guid.NewGuid(), new(owner), ct)).IsSuccess, "Non-member cannot assign tasks");
    Require(await service.AssignTask(org, project.Id, first.Id, task.Id, owner, new(null), ct));
    db.ChangeTracker.Clear();
    Check(Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single(t => t.Id == task.Id).AssigneeId is null, "Unassignment persists");
    var defaults = Require(await service.Get(org, project.Id, first.Id, owner, ct));
    Check(defaults.Columns.Select(c => c.Id).SequenceEqual(new[] { "pending", "progress", "done" }), "Every TODO starts with three default columns");
    var customColumns = new TodoColumn[] { new("pending", "Backlog"), new("progress", "Building"), new("review", "Review"), new("done", "Shipped"), new("archive", "Archived") };
    var customized = Require(await service.ConfigureColumns(org, project.Id, first.Id, owner, new(customColumns, defaults.BoardRevision), ct));
    Check(!(await service.ConfigureColumns(org, project.Id, first.Id, owner, new(customColumns, defaults.BoardRevision), ct)).IsSuccess, "Stale column edit rejected");
    Check(!(await service.ConfigureColumns(org, project.Id, first.Id, owner, new([], customized.BoardRevision), ct)).IsSuccess, "Zero columns rejected");
    Check(!(await service.ConfigureColumns(org, project.Id, first.Id, owner, new([..customColumns, new("sixth", "Sixth")], customized.BoardRevision), ct)).IsSuccess, "Six columns rejected");
    Check(!(await service.ConfigureColumns(org, project.Id, first.Id, owner, new([new("same", "One"), new("same", "Two")], customized.BoardRevision), ct)).IsSuccess, "Duplicate column IDs rejected");
    Check(!(await service.ConfigureColumns(org, project.Id, first.Id, owner, new([new("pending", "   ")], customized.BoardRevision), ct)).IsSuccess, "Blank column name rejected");
    Check(!(await service.ConfigureColumns(org, Guid.NewGuid(), first.Id, owner, new(customColumns, customized.BoardRevision), ct)).IsSuccess, "Cross-project column edit rejected");
    Check(!(await service.ConfigureColumns(org, project.Id, first.Id, Guid.NewGuid(), new(customColumns, customized.BoardRevision), ct)).IsSuccess, "Non-member column edit rejected");
    Require(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("review"), ct));
    var customTask = Require(await service.AddTask(org, project.Id, first.Id, owner, new("Custom column task", null, "archive", AssigneeIds: [owner, owner]), ct));
    Check(customTask.AssigneeIds.Count == 1, "Duplicate assignees deduplicated at creation");
    Require(await service.ChangeAssignee(org, project.Id, first.Id, task.Id, owner, new(owner), ct));
    Require(await service.ChangeAssignee(org, project.Id, first.Id, task.Id, owner, new(owner), ct));
    Check(!(await service.ChangeAssignee(org, project.Id, first.Id, task.Id, owner, new(Guid.NewGuid()), ct)).IsSuccess, "Non-member multi-assignee rejected");
    Check(!(await service.ChangeAssignee(org, project.Id, second.Id, task.Id, owner, new(owner), ct)).IsSuccess, "Cross-board multi-assignment rejected");
    db.ChangeTracker.Clear();
    var persisted = Require(await service.Get(org, project.Id, first.Id, owner, ct));
    Check(persisted.Columns.SequenceEqual(customColumns), "Custom names and order persist after reload");
    Check(persisted.Tasks.Single(t => t.Id == task.Id).AssigneeIds.SequenceEqual(new[] { owner }), "Repeated add is idempotent and persists");
    var unchanged = Require(await service.Get(org, project.Id, second.Id, owner, ct));
    Check(unchanged.Columns.Select(c => c.Name).SequenceEqual(new[] { "Pending", "In progress", "Done" }), "Customizing one TODO leaves the other unchanged");
    var reduced = Require(await service.ConfigureColumns(org, project.Id, first.Id, owner, new([new("pending", "Queue"), new("archive", "Finished")], persisted.BoardRevision), ct));
    Check(reduced.Tasks.Single(t => t.Id == task.Id).Status == "pending", "Tasks from removed columns move to nearest survivor, left on ties");
    Check(reduced.Tasks.Single(t => t.Id == customTask.Id).Status == "archive", "Tasks in retained columns stay put");
    Check(!(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("review"), ct)).IsSuccess, "Move to removed column rejected");
    Check(!(await service.AddTask(org, project.Id, first.Id, owner, new("Invalid column", null, "done"), ct)).IsSuccess, "Create in removed column rejected");
    var single = Require(await service.ConfigureColumns(org, project.Id, first.Id, owner, new([new("pending", "All finished")], reduced.BoardRevision), ct));
    Check(single.Tasks.All(t => t.Status == single.Columns[^1].Id), "Single-column board keeps all tasks in its completion column");
    var secondPerson = DevHub.Domain.Users.User.CreateExternal($"todo-smoke-{Guid.NewGuid():N}@example.test", $"todo{Guid.NewGuid():N}"[..20], "TODO", "Tester", null, DateTimeOffset.UtcNow);
    db.Users.Add(secondPerson);
    var testOrganization = await db.Organizations.Include(o => o.Members).Include(o => o.Roles).SingleAsync(o => o.Id == org);
    testOrganization.AddMember(secondPerson.Id, DateTimeOffset.UtcNow);
    foreach (var assignment in testOrganization.Members.Single(member => member.UserId == secondPerson.Id).RoleAssignments.ToArray()) testOrganization.RemoveRole(secondPerson.Id, assignment.RoleId);
    var viewerRole = testOrganization.AddRole("Date viewer", "#38bdf8", [DevHub.Domain.Organizations.OrganizationPermissions.ViewAllProjects], DateTimeOffset.UtcNow);
    testOrganization.AssignRole(secondPerson.Id, viewerRole.Id, DateTimeOffset.UtcNow);
    await db.SaveChangesAsync();
    var viewerBoard = Require(await service.Get(org, project.Id, second.Id, secondPerson.Id, ct));
    Check((await service.UpdateDates(org, project.Id, second.Id, secondPerson.Id, new(target, start, viewerBoard.BoardRevision), ct)).Error?.Type == ErrorType.Forbidden, "Read-only members cannot change TODO dates");
    Check((await service.AddTask(org, project.Id, second.Id, secondPerson.Id, new("Read-only task", "", "pending", TargetDate: target, StartDate: start), ct)).Error?.Type == ErrorType.Forbidden, "Read-only members cannot create dated tasks");
    Require(await service.ChangeAssignee(org, project.Id, first.Id, task.Id, owner, new(secondPerson.Id), ct));
    db.ChangeTracker.Clear();
    var twoAssignees = Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single(t => t.Id == task.Id).AssigneeIds;
    Check(twoAssignees.Count == 2 && twoAssignees.Contains(owner) && twoAssignees.Contains(secondPerson.Id), "Multiple different assignees persist together");
    Require(await service.ChangeAssignee(org, project.Id, first.Id, task.Id, owner, new(secondPerson.Id, true), ct));
    Check(Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single(t => t.Id == task.Id).AssigneeIds.SequenceEqual(new[] { owner }), "Removing one assignee preserves the other");
    Require(await service.ChangeAssignee(org, project.Id, first.Id, task.Id, owner, new(owner, true), ct));
    db.ChangeTracker.Clear();
    Check(Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single(t => t.Id == task.Id).AssigneeIds.Count == 0, "Avatar removal persists");
    var editable = Require(await service.AddTask(org, project.Id, second.Id, owner, new("Original title", "Original notes", "pending", AssigneeIds: [owner]), ct));
    var editBoard = Require(await service.Get(org, project.Id, second.Id, owner, ct));
    var editRequest = new UpdateTodoTaskRequest("  Updated title  ", "  Updated notes  ", "progress", [owner, secondPerson.Id, secondPerson.Id], editBoard.BoardRevision);
    Check((await service.UpdateTask(org, project.Id, second.Id, editable.Id, secondPerson.Id, editRequest with {TargetDate = target, StartDate = start}, ct)).Error?.Type == ErrorType.Forbidden, "Read-only members cannot edit task dates");
    Check(!(await service.UpdateTask(org, project.Id, second.Id, editable.Id, Guid.NewGuid(), editRequest, ct)).IsSuccess, "Non-member task edit rejected");
    Check(!(await service.UpdateTask(Guid.NewGuid(), project.Id, second.Id, editable.Id, owner, editRequest, ct)).IsSuccess, "Cross-organization task edit rejected");
    Check(!(await service.UpdateTask(org, project.Id, first.Id, editable.Id, owner, editRequest, ct)).IsSuccess, "Cross-board task edit rejected");
    Check(!(await service.UpdateTask(org, project.Id, second.Id, editable.Id, owner, editRequest with {Title = "   "}, ct)).IsSuccess, "Blank edit title rejected");
    Check(!(await service.UpdateTask(org, project.Id, second.Id, editable.Id, owner, editRequest with {Title = new string('x', 151)}, ct)).IsSuccess, "Long edit title rejected");
    Check(!(await service.UpdateTask(org, project.Id, second.Id, editable.Id, owner, editRequest with {Status = "missing"}, ct)).IsSuccess, "Unknown edit column rejected");
    Check(!(await service.UpdateTask(org, project.Id, second.Id, editable.Id, owner, editRequest with {AssigneeIds = [Guid.NewGuid()]}, ct)).IsSuccess, "New non-member edit assignee rejected");
    Require(await service.UpdateTask(org, project.Id, second.Id, editable.Id, owner, editRequest, ct));
    db.ChangeTracker.Clear();
    var editedBoard = Require(await service.Get(org, project.Id, second.Id, owner, ct));
    var editedTask = editedBoard.Tasks.Single(t => t.Id == editable.Id);
    Check(editedTask.Title == "Updated title" && editedTask.Description == "Updated notes" && editedTask.Status == "progress", "Task details edit persists after reload");
    Check(editedTask.AssigneeIds.Count == 2 && editedTask.AssigneeIds.Contains(owner) && editedTask.AssigneeIds.Contains(secondPerson.Id), "Edit assignees persist and deduplicate");
    Check(editedBoard.BoardRevision == editBoard.BoardRevision + 1, "Task edit increments board revision");
    Check(!(await service.UpdateTask(org, project.Id, second.Id, editable.Id, owner, editRequest, ct)).IsSuccess, "Stale task edit rejected");
    Check(!(await service.DeleteTask(org, project.Id, second.Id, editable.Id, owner, new(editBoard.BoardRevision), ct)).IsSuccess, "Stale task delete rejected");
    Check(!(await service.DeleteTask(org, project.Id, first.Id, editable.Id, owner, new(editedBoard.BoardRevision), ct)).IsSuccess, "Cross-board task delete rejected");
    Check(!(await service.DeleteTask(org, Guid.NewGuid(), second.Id, editable.Id, owner, new(editedBoard.BoardRevision), ct)).IsSuccess, "Cross-project task delete rejected");
    Check(!(await service.DeleteTask(org, project.Id, second.Id, editable.Id, Guid.NewGuid(), new(editedBoard.BoardRevision), ct)).IsSuccess, "Non-member task delete rejected");
    Require(await service.DeleteTask(org, project.Id, second.Id, editable.Id, owner, new(editedBoard.BoardRevision), ct));
    db.ChangeTracker.Clear();
    var deletedBoard = Require(await service.Get(org, project.Id, second.Id, owner, ct));
    Check(deletedBoard.Tasks.All(t => t.Id != editable.Id), "Deleted task stays removed after reload");
    Check(deletedBoard.BoardRevision == editedBoard.BoardRevision + 1, "Task delete increments board revision");
    Check(!await db.Set<TodoTaskAssignee>().AnyAsync(a => a.TaskId == editable.Id), "Task deletion removes assignee records");
    Check(!(await service.DeleteTask(org, project.Id, second.Id, editable.Id, owner, new(deletedBoard.BoardRevision), ct)).IsSuccess, "Already deleted task returns missing");
    var completionBoard = Require(await service.Create(org, project.Id, owner, new("Completion tracking", null, null), ct));
    var completionTask = Require(await service.AddTask(org, project.Id, completionBoard.Id, owner, new("Complete once", "", "pending"), ct));
    var done = Require(await service.MoveTask(org, project.Id, completionBoard.Id, completionTask.Id, owner, new("done"), ct));
    db.ChangeTracker.Clear();
    var finishedBoard = Require(await service.Get(org, project.Id, completionBoard.Id, owner, ct));
    Check(done.FinishedAtUtc is not null && finishedBoard.FinishedAtUtc == done.FinishedAtUtc &&
        finishedBoard.Tasks.Single().FinishedAtUtc == done.FinishedAtUtc, "Task and TODO record the same completion transition after reload");
    var renamedAfterCompletion = Require(await service.UpdateTask(org, project.Id, completionBoard.Id, completionTask.Id, owner,
        new("Renamed after completion", "", "done", [], finishedBoard.BoardRevision), ct));
    Check(renamedAfterCompletion.FinishedAtUtc == done.FinishedAtUtc, "Editing a finished task keeps its original completion date");
    Require(await service.MoveTask(org, project.Id, completionBoard.Id, completionTask.Id, owner, new("pending"), ct));
    db.ChangeTracker.Clear();
    var reopened = Require(await service.Get(org, project.Id, completionBoard.Id, owner, ct));
    Check(reopened.FinishedAtUtc is null && reopened.Tasks.Single().FinishedAtUtc is null, "Reopening clears current completion dates");
    Console.WriteLine("TODO persistence smoke checks passed.");
}
finally
{
    await transaction.RollbackAsync();
    Console.WriteLine("Test writes rolled back.");
}

static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new Exception(result.Error!.Code);
static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}
