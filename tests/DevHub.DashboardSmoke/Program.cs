using System.Reflection;
using System.Text.Json;
using DevHub.Application;
using DevHub.Application.Organizations;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Every write, including migration verification, uses a disposable local database.
var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
await using var isolated = new IsolatedSmokeDatabase(configuration);
var services = new ServiceCollection();
services.AddLogging(); services.AddApplication(); services.AddInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var sender = scope.ServiceProvider.GetRequiredService<ISender>();
var beforeDashboardReset = db.Database.GetMigrations()
    .TakeWhile(migration => !migration.EndsWith("_ResetDashboardLayoutsForNewDefaults", StringComparison.Ordinal)).Last();
await isolated.Seed(db, beforeDashboardReset);
var now = DateTimeOffset.UtcNow;
var widgets = new[] {new DashboardWidgetDefinition("private-note", "text", "half", "Only this member", 1, 2)};
await db.Database.ExecuteSqlAsync($"UPDATE [Organizations] SET [DashboardLayoutJson] = {JsonSerializer.Serialize(widgets)}, [DashboardPublishedAtUtc] = {now}");
await db.Database.ExecuteSqlAsync($"UPDATE [OrganizationMembers] SET [PersonalDashboardLayoutJson] = '[]', [PersonalDashboardSavedAtUtc] = {now}");
await db.GetService<IMigrator>().MigrateAsync();
Check(!await db.Organizations.AnyAsync(item => item.DashboardPublishedAtUtc != null || item.DashboardLayoutJson != null), "migration resets previous organization layouts to the default state");
Check(!await db.Set<OrganizationMember>().AnyAsync(item => item.PersonalDashboardSavedAtUtc != null || item.PersonalDashboardLayoutJson != null), "migration resets previous blank personal layouts to the default state");
var org = await db.Organizations.Include(o => o.Members).Include(o => o.Roles).SingleAsync();
var member = User.Create("member@example.test", "dashboard_member", "Dashboard", "Member", "test-hash", now);
var other = User.Create("other@example.test", "dashboard_other", "Dashboard", "Other", "test-hash", now);
db.AddRange(member, other);
org.AddMember(member.Id, now); org.AddMember(other.Id, now);
var secondOrg = Organization.Create("Another organization", null, member.Id, now);
db.Add(secondOrg);
await db.SaveChangesAsync();
var orgId = org.Id;
var ownerId = org.OwnerUserId;
Check(!org.HasPermission(member.Id, OrganizationPermissions.ManageDashboard), "regular member has no shared-dashboard permission");
var saved = await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, widgets, WidgetGap: 8));
Check(saved.IsSuccess && saved.Value!.SavedAtUtc is not null, "regular member saves a personal layout");
await db.GetService<IMigrator>().MigrateAsync();
db.ChangeTracker.Clear();
var loaded = await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id));
Check(loaded.IsSuccess && loaded.Value!.Widgets.Single().Content == "Only this member", "personal layout survives a fresh database read");
Check(loaded.Value!.WidgetGap == 8, "personal widget spacing survives a fresh database read");
foreach (var userId in new[] {ownerId, other.Id})
{
    var separate = await sender.Send(new GetPersonalDashboardQuery(orgId, userId));
    Check(separate.IsSuccess && separate.Value!.SavedAtUtc is null && separate.Value.Widgets.Count == 0, "another member, including the owner, has a separate layout");
}
var otherOrgLayout = await sender.Send(new GetPersonalDashboardQuery(secondOrg.Id, member.Id));
Check(otherOrgLayout.IsSuccess && otherOrgLayout.Value!.Widgets.Count == 0, "personal layouts are scoped to organization");
var outsider = Guid.NewGuid();
Check(!(await sender.Send(new GetPersonalDashboardQuery(orgId, outsider))).IsSuccess, "nonmembers cannot read personal dashboards");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, outsider, widgets))).IsSuccess, "nonmembers cannot save personal dashboards");
Check(!(await sender.Send(new PublishOrganizationDashboardCommand(orgId, member.Id, widgets))).IsSuccess, "personal editing grants no organization publishing rights");
var shared = await sender.Send(new GetOrganizationDashboardQuery(orgId, member.Id));
Check(shared.IsSuccess && shared.Value!.PublishedAtUtc is null && shared.Value.Widgets.Count == 0, "personal saves leave the organization dashboard untouched");
Check(shared.Value!.WidgetGap is null, "a personal widget gap does not set the organization gap");
Check((await sender.Send(new PublishOrganizationDashboardCommand(orgId, ownerId, widgets, WidgetGap: 24))).IsSuccess, "the dashboard manager can publish the organization widget gap");
Check(!(await sender.Send(new PublishOrganizationDashboardCommand(orgId, ownerId, widgets, WidgetGap: 25))).IsSuccess, "the organization widget gap has an upper bound");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, widgets, WidgetGap: 7))).IsSuccess, "the personal widget gap has a lower bound");
db.ChangeTracker.Clear();
Check((await sender.Send(new GetOrganizationDashboardQuery(orgId, member.Id))).Value!.WidgetGap == 24, "members load the published organization gap independently of their personal gap");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [widgets[0] with {Type = "unsupported"}]))).IsSuccess, "invalid widgets are rejected");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [widgets[0], widgets[0]]))).IsSuccess, "duplicate widget identifiers are rejected");
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.Widgets.Single().Content == "Only this member", "failed saves preserve the prior layout");
var chips = new[]
{
    new DashboardWidgetDefinition("teams-one", "link-teams-chip", "third", null, 0.5, 1),
    new DashboardWidgetDefinition("teams-two", "link-teams-chip", "third", null, 0.5, 1)
};
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, chips))).IsSuccess, "widget types can repeat with distinct identifiers");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [chips[0] with {Width = 2}]))).IsSuccess, "fixed quick-link dimensions are enforced");
db.ChangeTracker.Clear();
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.Widgets.Count == 2, "repeat widget instances survive reload");
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.WidgetGap == 8, "older clients can save widgets without overwriting the gap");
var cards = Enumerable.Range(0, 4).Select(index =>
    new DashboardWidgetDefinition($"link-{index}", "link-teams-card", "third", null, 1, 1.25, "links", index * 2.5, 0)).ToArray();
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, cards))).IsSuccess, "four Quick Link cards fit on the 5-unit grid");
db.ChangeTracker.Clear();
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.Widgets.Last().Column == 7.5, "the fourth Quick Link card position survives reload");
var fiveChips = Enumerable.Range(0, 5).Select(index => chips[0] with { Id = $"chip-{index}", SectionId = "chips", Column = index * 2, Row = 0 }).ToArray();
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, fiveChips))).IsSuccess, "five Quick Link chips fit on the 5-unit grid");
var compactText = new DashboardWidgetDefinition("compact-text", "text-rows-2", "half", """{"rows":[{"text":"Heading"},{"text":"Note"}]}""", 0.5, 2);
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [compactText]))).IsSuccess, "two text rows fit in a half-height widget");
db.ChangeTracker.Clear();
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.Widgets.Single().Height == 0.5, "half-height text widget keeps its size after reload");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [compactText with { Height = 0.25 }]))).IsSuccess, "quarter-unit heights are rejected");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [compactText with { Column = 0, Row = 1.5 }]))).IsSuccess, "fractional row positions are rejected");
const string imageData = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aP2sAAAAASUVORK5CYII=";
var embeddedImage = JsonSerializer.Serialize(new { url = imageData, fit = "contain" });
var image = new DashboardWidgetDefinition("uploaded-image", "image-full", "full", embeddedImage, 4, 5, "image-section", 0, 0);
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [image]))).IsSuccess, "an uploaded image can span the dashboard width and four height units");
db.ChangeTracker.Clear();
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.Widgets.Single() == image, "uploaded image, fit, and dimensions survive a fresh database read");
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [image with { Width = 0.5, Height = 0.5 }]))).IsSuccess, "image frames can shrink to half a unit in both directions");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [image with { Content = embeddedImage.Replace("image/png", "image/svg+xml") }]))).IsSuccess, "embedded SVGs are rejected");
var styledImage = image with { Appearance = new DashboardWidgetAppearance(24, "primary", 60) };
var styledNote = compactText with { Appearance = new DashboardWidgetAppearance(0, "#265A73", 0) };
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [styledImage, styledNote]))).IsSuccess, "widgets save with theme colors and custom colors");
db.ChangeTracker.Clear();
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.Widgets.SequenceEqual([styledImage, styledNote]), "widget appearance and content survive a fresh database read");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [image with { Appearance = new DashboardWidgetAppearance(Color: "url(example)") }]))).IsSuccess, "unsupported widget background colors are rejected");
var captionedImage = image with
{
    Type = "image-left",
    Content = JsonSerializer.Serialize(new
    {
        url = imageData, fit = "contain", caption = new string('a', 250),
        captionFormat = new
        {
            align = "left",
            runs = Enumerable.Range(0, 250).Select(index => new { text = "a", bold = true, color = $"#{index:x6}" })
        }
    })
};
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [captionedImage]))).IsSuccess, "an uploaded image can have a separately formatted run for each caption character");
db.ChangeTracker.Clear();
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.Widgets.Single() == captionedImage, "caption formatting survives a fresh database read");
var graphs = new[]
{
    new DashboardWidgetDefinition("comparison", "graph-todos", "third", null, 1, 1.5),
    new DashboardWidgetDefinition("pie", "graph-bug-status", "half", null, 1.5, 2),
    new DashboardWidgetDefinition("activity", "graph-task-activity", "half", null, 1, 2)
};
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, graphs))).IsSuccess, "older clients can still save their original fixed chart dimensions");
var halfWidthGraphs = graphs.Select((graph, index) => graph with { Size = "half", Width = 2.5, Column = index % 2 * 5, Row = index / 2 * 3 });
var memberCounts = new DashboardWidgetDefinition("member-counts", "info-org-members", "third", null, 1, 1.5, "summary", 7, 0);
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [memberCounts with { Height = 0.5 }]))).IsSuccess, "older clients can still save the compact members widget");
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [memberCounts, .. halfWidthGraphs]))).IsSuccess, "the member presence tracker and half-width graphs save on the 5-unit canvas");
db.ChangeTracker.Clear();
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.Widgets.First() == memberCounts, "the member presence tracker survives reload");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [graphs[0] with { Width = 2, Height = 1.5 }]))).IsSuccess, "fixed graph dimensions are enforced per chart");
Check((await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, []))).IsSuccess, "intentionally empty layouts can be saved");
db.ChangeTracker.Clear();
var empty = await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id));
Check(empty.IsSuccess && empty.Value!.SavedAtUtc is not null && empty.Value.Widgets.Count == 0, "empty saved layout is distinct from first use");
await db.Set<OrganizationMember>().Where(m => m.OrganizationId == orgId && m.UserId == member.Id).ExecuteDeleteAsync();
db.ChangeTracker.Clear();
Check(!(await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).IsSuccess, "membership revocation removes personal-dashboard access");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, widgets))).IsSuccess, "revoked members cannot write a personal dashboard");
Console.WriteLine("Personal dashboard SQL checks passed.");

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine($"PASS {message}");
}
