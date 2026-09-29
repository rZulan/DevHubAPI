using System.Diagnostics;
using System.Reflection;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using DevHub.Api.Realtime;

await MembershipNotifications.Run();
await SubscriptionRevocation.Run();
var removalTracker = new WorkshopPresenceTracker(TimeProvider.System);
var removalOrg = Guid.NewGuid();
var retainedOrg = Guid.NewGuid();
var removedUser = Guid.NewGuid();
removalTracker.Join(removalOrg, removedUser, "removed-tab", "online");
removalTracker.Join(retainedOrg, removedUser, "removed-tab", "online");
removalTracker.Heartbeat(removalOrg, removedUser, "online");
if (!removalTracker.RemoveMember(removalOrg, removedUser) || removalTracker.RemoveMember(removalOrg, removedUser))
    throw new Exception("Revoked presence was not removed idempotently");
if (removalTracker.Join(removalOrg, Guid.NewGuid(), "observer", "online").Members.Any(m => m.UserId == removedUser))
    throw new Exception("Revoked member remained in a presence snapshot");
if (removalTracker.Leave("removed-tab").Single().OrganizationId != retainedOrg)
    throw new Exception("Removing organization membership damaged another organization's presence");
Console.WriteLine("PASS membership removal clears live and fallback presence without corrupting other memberships");
var clock = new TestClock();
var tracker = new WorkshopPresenceTracker(clock);
var orgA = Guid.NewGuid();
var orgB = Guid.NewGuid();
var alice = Guid.NewGuid();
var bob = Guid.NewGuid();
void Check(bool condition, string name)
{
    if (!condition) throw new InvalidOperationException(name);
    Console.WriteLine($"PASS {name}");
}
Check(tracker.Join(orgA, alice, "tab-a", "online").StatusChanged, "First join announces presence");
Check(!tracker.Join(orgA, alice, "tab-a", "online").StatusChanged, "Repeated join is idempotent");
tracker.Join(orgB, alice, "tab-a", "online");
var disconnected = tracker.Leave("tab-a");
Check(disconnected.Count == 2 && disconnected.All(result => result.IsNowOffline), "One socket leaves every joined organization");
Check(tracker.Leave("tab-a").Count == 0, "Duplicate disconnect is harmless");
tracker.Join(orgA, alice, "tab-a", "online");
tracker.Join(orgA, alice, "tab-b", "online");
Check(tracker.Leave("tab-a").Count == 0, "Closing one tab keeps another online");
Check(tracker.Join(orgA, alice, "tab-c", "dnd").StatusChanged, "Second tab status changes are broadcast");
Check(tracker.SetStatus(orgA, alice, "tab-b", "dnd") is null, "Unchanged status produces no broadcast");
Check(tracker.SetStatus(orgA, alice, "unknown", "invisible") is null, "Unjoined socket cannot change presence");
Check(tracker.SetStatus(orgA, alice, "tab-b", "invisible") == "offline", "Invisible status is exposed as offline");
Check(!tracker.Join(orgA, bob, "bob", "online").Members.Any(member => member.UserId == alice), "Invisible members are omitted from snapshots");
tracker.Leave("tab-b"); tracker.Leave("tab-c"); tracker.Leave("bob");
Check(tracker.Heartbeat(orgA, alice, "online").StatusChanged, "Fallback arrival announces presence to live clients");
Check(!tracker.Heartbeat(orgA, alice, "online").StatusChanged, "Repeated heartbeat does not fan out");
clock.Advance(TimeSpan.FromSeconds(119));
Check(tracker.ExpireHeartbeats().Count == 0, "Background tabs retain their lease");
clock.Advance(TimeSpan.FromSeconds(2));
Check(tracker.ExpireHeartbeats().Single().IsNowOffline, "Expired fallback announces offline without another request");
Check(tracker.ExpireHeartbeats().Count == 0, "Expired entries are reclaimed once");
tracker.Heartbeat(orgA, alice, "online");
tracker.Join(orgA, alice, "recovered", "online");
Check(tracker.Leave("recovered").Single().IsNowOffline, "Recovery transfers heartbeat lease to live socket");
tracker.Join(orgA, alice, "live", "online");
clock.Advance(TimeSpan.FromHours(1));
Check(tracker.ExpireHeartbeats().Count == 0, "Healthy sockets need no application heartbeat");
tracker.Leave("live");

var watch = Stopwatch.StartNew();
Parallel.For(0, 2000, index => {
    var organization = Guid.NewGuid();
    var user = Guid.NewGuid();
    var connection = $"load-{index}";
    tracker.Join(organization, user, connection, "online");
    tracker.Heartbeat(organization, user, "away");
    tracker.Join(organization, user, connection, "dnd");
    if (!tracker.Leave(connection).Single().IsNowOffline) throw new InvalidOperationException("Lost concurrent disconnect");
});
Check(tracker.ExpireHeartbeats().Count == 0, "Concurrent organizations leave no abandoned leases");
Console.WriteLine($"2,000 concurrent organization lifecycles: {watch.ElapsedMilliseconds} ms (local smoke, not capacity certification)");

if (args.Contains("--database"))
{
    // Read-only checks against the locally configured database; no test records are written.
    var configuration = new ConfigurationBuilder()
        .SetBasePath(Path.GetFullPath("src/DevHub.Api"))
        .AddJsonFile("appsettings.json")
        .AddJsonFile("appsettings.Development.json", optional: true)
        .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true)
        .AddEnvironmentVariables().Build();
    var services = new ServiceCollection();
    services.AddLogging();
    services.AddInfrastructure(configuration);
    await using var provider = services.BuildServiceProvider();
    await using var scope = provider.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var repository = scope.ServiceProvider.GetRequiredService<IOrganizationRepository>();
    var organization = await db.Organizations.AsNoTracking().FirstAsync();
    var owner = organization.OwnerUserId;
    Check(await repository.IsMemberAsync(organization.Id, owner), "Database membership existence query authorizes owner");
    var outsider = Guid.NewGuid();
    Check(!await repository.IsMemberAsync(organization.Id, outsider), "Database membership query rejects nonmember");
    var summaries = await repository.ListSummariesForUserAsync(owner);
    var original = await repository.GetByIdAsync(organization.Id);
    var summary = summaries.Single(item => item.Id == organization.Id);
    Check(summary.MemberCount == original!.Members.Count && summary.TeamCount == original.Teams.Count,
        "Projected organization counts match the aggregate");
    var dashboard = await repository.GetDashboardForMemberAsync(organization.Id, owner);
    Check(dashboard is not null && dashboard.Members.Count == 0 && dashboard.Teams.Count == 0,
        "Dashboard read does not materialize members or teams");
    Check(await repository.GetDashboardForMemberAsync(organization.Id, outsider) is null,
        "Dashboard read rejects nonmember");
    var membership = await repository.GetMembershipForUserAsync(organization.Id, owner);
    Check(membership!.Members.Count == original.Members.Count && membership.Teams.Count == 0,
        "Member read preserves membership while omitting teams");
    Check(await repository.GetMembershipForUserAsync(organization.Id, outsider) is null,
        "Member read rejects nonmember");
}

sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 9, 0, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan value) => _now += value;
}
