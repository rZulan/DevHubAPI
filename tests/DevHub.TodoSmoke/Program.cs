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
services.AddLogging();
services.AddInfrastructure(configuration);
// Rollback-based checks use one explicit transaction, without retrying it piecemeal.
services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(configuration.GetConnectionString("DefaultConnection"), sql => sql.ExecutionStrategy(dependencies => new Microsoft.EntityFrameworkCore.Storage.NonRetryingExecutionStrategy(dependencies))));
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var service = scope.ServiceProvider.GetRequiredService<ITodoService>();
await using var transaction = await db.Database.BeginTransactionAsync();
try
{
    var project = await db.Projects.AsNoTracking().FirstAsync();
    var owner = await db.Organizations.Where(o => o.Id == project.OrganizationId).Select(o => o.OwnerUserId).SingleAsync();
    var org = project.OrganizationId;
    var ct = CancellationToken.None;
    var target = new DateOnly(2026, 12, 31);
    var first = Require(await service.Create(org, project.Id, owner, new("Persistence smoke A", "Context", target), ct));
    var second = Require(await service.Create(org, project.Id, owner, new("Persistence smoke B", null, null), ct));
    var task = Require(await service.AddTask(org, project.Id, first.Id, owner, new("Saved task", "Task context", "pending"), ct));
    Require(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("progress"), ct));
    db.ChangeTracker.Clear();
    var reloaded = Require(await service.List(org, project.Id, owner, ct));
    Check(reloaded.Single(t => t.Id == first.Id).TargetDate == target, "Target date persists");
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
    await db.SaveChangesAsync();
    Require(await service.ChangeAssignee(org, project.Id, first.Id, task.Id, owner, new(secondPerson.Id), ct));
    db.ChangeTracker.Clear();
    var twoAssignees = Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single(t => t.Id == task.Id).AssigneeIds;
    Check(twoAssignees.Count == 2 && twoAssignees.Contains(owner) && twoAssignees.Contains(secondPerson.Id), "Multiple different assignees persist together");
    Require(await service.ChangeAssignee(org, project.Id, first.Id, task.Id, owner, new(secondPerson.Id, true), ct));
    Check(Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single(t => t.Id == task.Id).AssigneeIds.SequenceEqual(new[] { owner }), "Removing one assignee preserves the other");
    Require(await service.ChangeAssignee(org, project.Id, first.Id, task.Id, owner, new(owner, true), ct));
    db.ChangeTracker.Clear();
    Check(Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single(t => t.Id == task.Id).AssigneeIds.Count == 0, "Avatar removal persists");
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
