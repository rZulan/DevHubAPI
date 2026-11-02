using System.Reflection;
using DevHub.Application;
using DevHub.Application.Organizations;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
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
await isolated.Seed(db, db.Database.GetMigrations().Last());
var org = await db.Organizations.Include(o => o.Members).Include(o => o.Roles).SingleAsync();
var now = DateTimeOffset.UtcNow;
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
var widgets = new[] {new DashboardWidgetDefinition("private-note", "text", "half", "Only this member", 1, 2)};
var saved = await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, widgets));
Check(saved.IsSuccess && saved.Value!.SavedAtUtc is not null, "regular member saves a personal layout");
db.ChangeTracker.Clear();
var loaded = await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id));
Check(loaded.IsSuccess && loaded.Value!.Widgets.Single().Content == "Only this member", "personal layout survives a fresh database read");
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
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [widgets[0] with {Type = "unsupported"}]))).IsSuccess, "invalid widgets are rejected");
Check(!(await sender.Send(new SavePersonalDashboardCommand(orgId, member.Id, [widgets[0], widgets[0]]))).IsSuccess, "duplicate widget identifiers are rejected");
Check((await sender.Send(new GetPersonalDashboardQuery(orgId, member.Id))).Value!.Widgets.Single().Content == "Only this member", "failed saves preserve the prior layout");
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
