using System.Reflection;
using System.Text;
using System.Text.Json;
using DevHub.Application;
using DevHub.Application.BugReports;
using DevHub.Application.Common;
using DevHub.Application.Ideas;
using DevHub.Application.Organizations;
using DevHub.Application.ProjectTools;
using DevHub.Application.Todos;
using DevHub.Application.Workshops;
using DevHub.Domain.Notifications;
using DevHub.Domain.Organizations;
using DevHub.Domain.Projects;
using DevHub.Domain.Teams;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
await using var isolated = new IsolatedSmokeDatabase(configuration);
configuration["Security:Workshop:InvitesPerUserPerDay"] = "2";
configuration["Security:Workshop:InvitesPerWorkshopPerDay"] = "3";
configuration["Security:Workshop:CreatesPerMinute"] = "2";
configuration["Security:Workshop:NotificationsPerMinute"] = "3";
configuration["Security:Workshop:CanvasMaximumObjects"] = "2";
configuration["Security:Workshop:CanvasWarningObjects"] = "1";
configuration["Security:Workshop:AttachmentBytesPerWorkshop"] = "64";
configuration["Security:Workshop:AttachmentBytesTotal"] = "96";
var clock = new TestClock();
var services = new ServiceCollection(); services.AddLogging(); services.AddApplication(); services.AddInfrastructure(configuration);
services.RemoveAll<TimeProvider>(); services.AddSingleton<TimeProvider>(clock);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
await db.Database.MigrateAsync();
await isolated.Seed(db);
var source = await db.Projects.SingleAsync();
var owner = await db.Organizations.Select(o => o.OwnerUserId).SingleAsync();
var checks = 0;
void Check(bool value, string message) {if (!value) throw new Exception(message); Console.WriteLine($"PASS {message}"); checks++;}

var sender = scope.ServiceProvider.GetRequiredService<ISender>();
for (var i = 0; i < 2; i++) Check((await sender.Send(new CreateOrganizationInviteCommand(source.OrganizationId, owner))).IsSuccess, "Invite within daily budget succeeds");
var denied = await sender.Send(new CreateOrganizationInviteCommand(source.OrganizationId, owner));
Check(denied.Error is {Type: ErrorType.Throttled, RetryAfterSeconds: > 0}, "Daily invitation excess returns a cooldown");
await using (var restarted = services.BuildServiceProvider())
await using (var restartScope = restarted.CreateAsyncScope())
    Check((await restartScope.ServiceProvider.GetRequiredService<ISender>().Send(new CreateOrganizationInviteCommand(source.OrganizationId, owner))).Error?.Type == ErrorType.Throttled,
        "Invitation budget survives a fresh application service provider");
var usage = scope.ServiceProvider.GetRequiredService<IWorkshopUsage>();
Check(await usage.Reserve([new($"invite-workshop:{source.OrganizationId:N}", 3, 1, TimeSpan.FromDays(1))], default) is null,
    "Rejected multi-counter reservation did not consume workshop capacity");
Check(await usage.Reserve([new($"invite-workshop:{source.OrganizationId:N}", 3, 1, TimeSpan.FromDays(1))], default) is not null,
    "Workshop-wide invitation budget is enforced independently");
clock.Advance(TimeSpan.FromDays(1));
Check((await sender.Send(new CreateOrganizationInviteCommand(source.OrganizationId, owner))).IsSuccess, "Daily invite budget resets on a new UTC day");

var grants = await Task.WhenAll(Enumerable.Range(0, 12).Select(async _ =>
{
    await using var concurrent = provider.CreateAsyncScope();
    return await concurrent.ServiceProvider.GetRequiredService<IWorkshopUsage>().Take("concurrent-notices", 3, 1, TimeSpan.FromMinutes(1), default);
}));
Check(grants.Sum() == 3, "Concurrent independent contexts cannot exceed persistent notification budget");
for (var i = 0; i < 5; i++) db.UserNotifications.Add(new UserNotification(source.OrganizationId, owner, owner, "test", "notice", source.Id, Guid.NewGuid(), clock.GetUtcNow()));
var keptTeam = Team.Create(source.OrganizationId, owner, "Underlying work survives", null, clock.GetUtcNow());
db.Add(keptTeam); await db.SaveChangesAsync();
Check(await db.UserNotifications.CountAsync(n => n.ActorUserId == owner) == 3 && await db.Teams.AnyAsync(t => t.Id == keptTeam.Id),
    "Notification fan-out is capped while the underlying business operation still saves");
db.UserNotifications.Add(new UserNotification(source.OrganizationId, owner, owner, "test", "overflow", source.Id, Guid.NewGuid(), clock.GetUtcNow()));
await db.SaveChangesAsync();
Check(await db.UserNotifications.CountAsync(n => n.ActorUserId == owner) == 3, "Repeated notification producers share the same persistent budget");

var todos = scope.ServiceProvider.GetRequiredService<ITodoService>();
var board = await todos.Create(source.OrganizationId, source.Id, owner, new("Limited board", "", null), default);
Check(board.IsSuccess, "First TODO creation succeeds");
Check((await todos.AddTask(source.OrganizationId, source.Id, board.Value!.Id, owner, new("Task", "", "pending"), default)).IsSuccess, "Task shares the creation budget");
Check((await todos.Create(source.OrganizationId, source.Id, owner, new("Excess", "", null), default)).Error?.Type == ErrorType.Throttled, "TODO creation excess is rejected");
var tools = scope.ServiceProvider.GetRequiredService<IProjectToolsService>();
var notes = JsonSerializer.SerializeToElement(new {notes = new[] {new {id = "n1", title = "Note", body = "", color = "#ffffff", updatedAt = "2026-10-30T00:00:00Z"}}});
Check((await tools.Save(source.OrganizationId, source.Id, owner, "notes", new(0, notes), default)).Error?.Type == ErrorType.Throttled,
    "Notes cannot bypass the shared task/note creation budget");

JsonElement Shapes(int count) => JsonSerializer.SerializeToElement(Enumerable.Range(0, count).Select(i => new {
    id = $"s{i}", kind = "rectangle", x = 0, y = 0, width = 100, height = 100, cornerRadius = 0, outlineWidth = 1, fontSize = 16,
    appearance = "fill", outlineStyle = "solid", textAlign = "left", verticalAlign = "top", fontFamily = "Arial",
    fillColor = "#ffffff", outlineColor = "#000000", textColor = "auto", text = "", bold = false, italic = false, underline = false, strikethrough = false
}));
var ideas = scope.ServiceProvider.GetRequiredService<IIdeasService>();
Check((await ideas.Save(source.OrganizationId, source.Id, owner, new(0, Shapes(2)), default)).IsSuccess, "Canvas at capacity still saves");
Check((await ideas.Save(source.OrganizationId, source.Id, owner, new(1, Shapes(3)), default)).Error?.Code == "Workshop.CanvasCapacity", "New canvas cannot exceed its object limit");
await db.ProjectDocuments.Where(d => d.ProjectId == source.Id && d.ToolType == "ideas").ExecuteUpdateAsync(update => update.SetProperty(d => d.ContentJson, Shapes(3).GetRawText()));
Check((await ideas.Save(source.OrganizationId, source.Id, owner, new(1, Shapes(3)), default)).IsSuccess, "Existing oversized canvas remains editable");
Check((await ideas.Save(source.OrganizationId, source.Id, owner, new(2, Shapes(4)), default)).Error?.Code == "Workshop.CanvasCapacity", "Existing oversized canvas cannot grow further");
Check((await ideas.Save(source.OrganizationId, source.Id, owner, new(2, Shapes(1)), default)).IsSuccess, "Existing oversized canvas can be reduced");

SubmitBugRequest Upload() => new(Guid.NewGuid(), "reporter@example.test", "Storage test", "Test", "", "", null, "medium", [new("test.txt", Encoding.UTF8.GetBytes(new string('a', 40)))]);
var concurrentUploads = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
{
    await using var upload = provider.CreateAsyncScope();
    return await upload.ServiceProvider.GetRequiredService<IBugReportsService>().SubmitTeam(new(source.OrganizationId, source.Id, owner), Upload(), default);
}));
Check(concurrentUploads.Count(result => result.IsSuccess) == 1 && concurrentUploads.Count(result => result.Error?.Code == "Workshop.StorageCapacity") == 1,
    "Concurrent uploads cannot race past the workshop storage quota");
var stored = await scope.ServiceProvider.GetRequiredService<IWorkshopStorage>().Get(source.OrganizationId, default);
Check(stored.UsedBytes == 40 && stored.LimitBytes == 64, "Storage usage counts persisted attachment bytes");
var secondOrg = Organization.Create("Second quota workshop", null, owner, clock.GetUtcNow());
var secondTeam = Team.Create(secondOrg.Id, owner, "Team", null, clock.GetUtcNow());
var secondProject = Project.Create(secondOrg.Id, secondTeam.Id, owner, "Second project", "quota", ProjectStatus.Planning, [], null, null, null, null, null, null, [], null, null, clock.GetUtcNow());
db.AddRange(secondOrg, secondTeam, secondProject); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
var reports = scope.ServiceProvider.GetRequiredService<IBugReportsService>();
Check((await reports.SubmitTeam(new(secondOrg.Id, secondProject.Id, owner), Upload(), default)).IsSuccess, "Another workshop can use its own remaining storage");
var oneMore = Upload() with {Attachments = [new("test.txt", Encoding.UTF8.GetBytes(new string('b', 20)))]};
Check((await reports.SubmitTeam(new(secondOrg.Id, secondProject.Id, owner), oneMore, default)).Error?.Code == "Workshop.StorageCapacity", "Service-wide storage budget also prevents excess uploads");
Check(await db.BugReports.CountAsync() == 2, "Rejected uploads leave no report or attachment rows");
Console.WriteLine($"{checks} workshop limit checks passed.");

sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = new(2026, 10, 30, 12, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan amount) => now += amount;
}
