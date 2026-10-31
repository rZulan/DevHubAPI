using System.Reflection;
using System.Text.Json;
using DevHub.Application;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Notifications;
using DevHub.Application.ProjectTools;
using DevHub.Application.Projects;
using DevHub.Application.BugReports;
using DevHub.Application.Teams;
using DevHub.Application.Todos;
using DevHub.Domain.Notifications;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Creates and removes its own local SQL database. Never writes application data.
var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
await using var isolated = new IsolatedSmokeDatabase(configuration);
// Exercise delivery/pagination independently of the quota-boundary suite.
configuration["Security:Workshop:NotificationsPerMinute"] = "200";
var services = new ServiceCollection(); services.AddLogging(); services.AddApplication(); services.AddInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
await isolated.Seed(db, "20261027034254_AddWorkshopNotifications", async (context, _) => await context.Database.MigrateAsync());
var repository = scope.ServiceProvider.GetRequiredService<IOrganizationRepository>();
var project = await db.Projects.SingleAsync();
var organization = (await repository.GetByIdAsync(project.OrganizationId))!;
var ownerId = organization.OwnerUserId;
var member = User.Create("member@example.test", "notification_member", "Team", "Member", "test-only-hash", DateTimeOffset.UtcNow);
var outsider = User.Create("outsider@example.test", "notification_outsider", "Other", "Member", "test-only-hash", DateTimeOffset.UtcNow);
db.Users.AddRange(member, outsider);
organization.AddMember(member.Id, DateTimeOffset.UtcNow);
foreach (var role in organization.Members.Single(m => m.UserId == member.Id).RoleAssignments.ToArray()) organization.RemoveRole(member.Id, role.RoleId);
var team = organization.Teams.Single(t => t.Id == project.TeamId);
team.AddMember(member.Id, DateTimeOffset.UtcNow);
await db.SaveChangesAsync();
var inbox = scope.ServiceProvider.GetRequiredService<INotificationService>();
var activity = scope.ServiceProvider.GetRequiredService<IWorkshopActivity>();
var todos = scope.ServiceProvider.GetRequiredService<ITodoService>();
var sender = scope.ServiceProvider.GetRequiredService<ISender>();
var orgId = organization.Id;
void Check(bool pass, string name) {if (!pass) throw new Exception(name); Console.WriteLine($"PASS {name}");}
async Task<NotificationPageResponse> List(Guid? user = null, bool unreadOnly = false, long? before = null)
{
    db.ChangeTracker.Clear();
    var response = await inbox.List(orgId, user ?? member.Id, before, unreadOnly, default);
    if (!response.IsSuccess) throw new Exception(response.Error!.Description);
    return response.Value!;
}

Check((await List()).Items.Count == 0, "Fresh user inbox is empty");
var todoResult = await todos.Create(orgId, project.Id, ownerId, new("Release board", "", null), default);
Check(todoResult.IsSuccess, "Board creation succeeds with activity in the same transaction");
var todo = todoResult.Value!;
Check((await List()).Items.Single().Kind == "todo-created", "Project members receive new-board activity");
Check((await List(ownerId)).Items.Count == 0, "Users are not notified about their own actions");
var taskResult = await todos.AddTask(orgId, project.Id, todo.Id, ownerId, new("Review release", "", "pending", AssigneeIds: [member.Id]), default);
Check(taskResult.IsSuccess, "Task creation succeeds");
var task = taskResult.Value!;
var assignment = (await List()).Items.Single(n => n.TargetId == task.Id);
Check(assignment.Kind == "task-assigned", "New assignee gets one personal notification without duplicate task activity");
Check(!(await inbox.MarkRead(orgId, ownerId, assignment.Id, default)).IsSuccess, "Another user cannot mark a notification read");
Check(!(await inbox.List(orgId, outsider.Id, null, false, default)).IsSuccess, "Non-members cannot list the inbox");
Check(!(await inbox.MarkAllRead(orgId, outsider.Id, assignment.Id, default)).IsSuccess, "Non-members cannot clear the inbox");
Check((await inbox.MarkRead(orgId, member.Id, assignment.Id, default)).IsSuccess, "Recipient can mark one notification read");
Check((await List()).Items.Single(n => n.Id == assignment.Id).ReadAtUtc is not null, "Read status persists after reloading the context");
Check((await List(unreadOnly: true)).Items.All(n => n.ReadAtUtc is null), "Unread filter excludes read rows");

await todos.MoveTask(orgId, project.Id, todo.Id, task.Id, ownerId, new("progress"), default);
var updateCount = (await List()).Items.Count(n => n.Kind == "task-updated");
await todos.MoveTask(orgId, project.Id, todo.Id, task.Id, ownerId, new("done"), default);
Check((await List()).Items.Count(n => n.Kind == "task-updated") == updateCount, "Repeated updates to the same item are grouped for five minutes");
var beforeUnchanged = await db.UserNotifications.CountAsync();
await todos.AssignTask(orgId, project.Id, todo.Id, task.Id, ownerId, new(member.Id), default);
await todos.MoveTask(orgId, project.Id, todo.Id, task.Id, ownerId, new("done"), default);
Check(await db.UserNotifications.CountAsync() == beforeUnchanged, "Unchanged assignments and statuses generate no activity");
var current = await todos.Get(orgId, project.Id, todo.Id, ownerId, default);
var count = await db.UserNotifications.CountAsync();
var conflict = await todos.UpdateTask(orgId, project.Id, todo.Id, task.Id, ownerId, new("Stale edit", "", "done", [member.Id], current.Value!.BoardRevision - 1), default);
Check(!conflict.IsSuccess && await db.UserNotifications.CountAsync() == count, "Rejected edits generate no notification");

var boundary = (await List()).LatestId;
await activity.Queue(new(orgId, ownerId, "project-updated", project.Name, project.Id, Guid.NewGuid(), project.Id), default);
await db.SaveChangesAsync();
await inbox.MarkAllRead(orgId, member.Id, boundary, default);
Check((await List(unreadOnly: true)).Items.Single().Id > boundary, "Mark-all-read preserves notifications arriving after the client snapshot");

organization = (await repository.GetByIdAsync(orgId))!;
organization.Teams.Single(t => t.Id == project.TeamId).RemoveMember(member.Id);
await db.SaveChangesAsync();
Check((await List()).Items.Count == 0, "Losing project access hides previous project notifications");
Check(!(await inbox.MarkRead(orgId, member.Id, assignment.Id, default)).IsSuccess, "Revoked project access also blocks notification mutations");
organization = (await repository.GetByIdAsync(orgId))!;
organization.Teams.Single(t => t.Id == project.TeamId).AddMember(member.Id, DateTimeOffset.UtcNow);
await db.SaveChangesAsync();
var teamChange = await sender.Send(new UpdateTeamCommand(orgId, team.Id, ownerId, ownerId, "Release team", "Updated team"));
Check(teamChange.IsSuccess && (await List()).Items.Any(n => n.Kind == "team-updated"), "Team metadata changes publish activity");

var beforeTools = await db.UserNotifications.CountAsync();
var tools = scope.ServiceProvider.GetRequiredService<IProjectToolsService>();
using var emptyWiki = JsonDocument.Parse("{\"homePageId\":null,\"pages\":[]}");
Check((await tools.Save(orgId, project.Id, ownerId, "wiki", new(0, emptyWiki.RootElement), default)).IsSuccess, "Wiki saves still work");
Check(await db.UserNotifications.CountAsync() == beforeTools, "Document saves do not create notifications");

var newProject = await sender.Send(new CreateProjectCommand(orgId, ownerId, team.Id, ownerId, "Notification project", "Lifecycle checks", "Planning", [], null, null, null, null, null, null, [], null, null));
Check(newProject.IsSuccess && (await List()).Items.Any(n => n.Kind == "project-created" && n.TargetId == newProject.Value!.Id), "New projects notify members of their team");
var unchangedProjectCount = await db.UserNotifications.CountAsync();
var unchangedProject = await sender.Send(new UpdateProjectCommand(orgId, newProject.Value!.Id, ownerId, team.Id, ownerId, "Notification project", "Lifecycle checks", "Planning", [], null, null, null, null, null, null, [], null, null));
Check(unchangedProject.IsSuccess && await db.UserNotifications.CountAsync() == unchangedProjectCount, "Saving unchanged project details generates no activity");
var deletedProject = await sender.Send(new DeleteProjectCommand(orgId, newProject.Value!.Id, ownerId));
Check(deletedProject.IsSuccess && (await List()).Items.Any(n => n.Kind == "project-deleted" && n.TargetId == newProject.Value!.Id), "Project deletion leaves a notification linking to the project list");

var bugs = scope.ServiceProvider.GetRequiredService<IBugReportsService>();
var bugScope = new BugScope(orgId, project.Id, ownerId);
var portal = await bugs.SavePortal(bugScope, true, false, -1, default);
Check(portal.IsSuccess, "Bug portal activity is recorded");
var submission = new SubmitBugRequest(Guid.NewGuid(), "reporter@example.test", "Public issue", "A useful reproduction", "Open the page", "Desktop browser", null, "medium", []);
var receipt = await bugs.SubmitPublic(portal.Value!.Token, submission, default);
Check(receipt.IsSuccess && (await List()).Items.Any(n => n.Kind == "bug-created" && n.ActorName == "Public reporter"), "Public bug reports notify project members");
var beforeRetry = await db.UserNotifications.CountAsync();
await bugs.SubmitPublic(portal.Value.Token, submission, default);
Check(await db.UserNotifications.CountAsync() == beforeRetry, "Retried bug submissions do not duplicate notifications");
var report = await db.BugReports.SingleAsync(r => r.Reference == receipt.Value!.Reference);
var triage = await bugs.Triage(bugScope, report.Id, new(report.Revision, "triaged", "high", null, "Private triage details"), default);
var bugActivity = (await List()).Items.Single(n => n.Kind == "bug-updated");
Check(triage.IsSuccess && bugActivity.TargetId == report.Id && !JsonSerializer.Serialize(bugActivity).Contains("Private triage details"), "Bug triage notifications do not expose internal notes");

for (var i = 0; i < 30; i++) db.UserNotifications.Add(new(orgId, member.Id, ownerId, "team-updated", $"Activity {i}", team.Id, Guid.NewGuid(), DateTimeOffset.UtcNow));
await db.SaveChangesAsync();
var first = await List();
var second = await List(before: first.NextCursor);
Check(first.Items.Count == 25 && first.NextCursor.HasValue && second.Items.Count > 0, "Inbox supports cursor pagination");
Check(!first.Items.Select(n => n.Id).Intersect(second.Items.Select(n => n.Id)).Any(), "Pagination does not repeat notifications");
Check(first.Items.All(n => n.Kind != "direct-message" && n.Kind != "chat-message" && !n.Kind.StartsWith("tool-")), "Chat and document-save events are excluded");
Check(!(await inbox.List(orgId, member.Id, -1, false, default)).IsSuccess, "Invalid cursors are rejected");
Console.WriteLine("Notification SQL checks passed.");
