using System.Security.Claims;
using DevHub.Api.Realtime;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Ideas;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

internal static class SubscriptionRevocation
{
    public static async Task Run()
    {
        var organization = Guid.NewGuid();
        var otherOrganization = Guid.NewGuid();
        var user = Guid.NewGuid();
        var project = Guid.NewGuid();
        var registry = new WorkshopSubscriptions();
        var presence = new TestConnection("presence");
        var canvas = new TestConnection("canvas");
        var otherTab = new TestConnection("other-tab");
        var unrelated = new TestConnection("unrelated");
        registry.Add(new(presence, organization, user, null));
        registry.Add(new(canvas, organization, user, project));
        registry.Add(new(otherTab, organization, user, null));
        registry.Add(new(unrelated, otherOrganization, user, null));
        var removed = new List<(string, string)>();
        var groups = Stub.Create<IGroupManager>((method, args) =>
        {
            if (method.Name != "RemoveFromGroupAsync") throw new Exception("Unexpected group call");
            removed.Add(((string)args![0]!, (string)args[1]!));
            return Task.CompletedTask;
        });
        var hub = Stub.Create<IHubContext<WorkshopHub>>((method, _) => method.Name == "get_Groups" ? groups : throw new Exception("Unexpected hub call"));
        var isMember = true;
        var canView = true;
        var organizations = Stub.Create<IOrganizationRepository>((method, _) => method.Name == "IsMemberAsync" ? Task.FromResult(isMember) : throw new Exception("Unexpected organization query"));
        var ideas = Stub.Create<IIdeasService>((method, _) => method.Name == "CanView" ? Task.FromResult(canView) : throw new Exception("Unexpected Ideas query"));
        var realtime = new WorkshopRealtime(registry, organizations, ideas, new WorkshopPresenceTracker(TimeProvider.System), hub);
        await realtime.Revalidate(organization);
        if (removed.Count != 0 || presence.Aborted || canvas.Aborted) throw new Exception("Authorized subscriptions were disconnected");
        canView = false;
        await realtime.Revalidate(organization);
        if (!canvas.Aborted || presence.Aborted || removed.Single() != ("canvas", WorkshopHub.GetIdeasGroupName(organization, project)))
            throw new Exception("Project access loss did not remove the canvas subscription only");
        isMember = false;
        await realtime.Revalidate(organization);
        if (!presence.Aborted || !otherTab.Aborted || unrelated.Aborted || removed.Count != 3 || registry.ForOrganization(organization).Length != 0)
            throw new Exception("Membership revocation did not remove every tab, or affected another organization");
        registry.Remove("unrelated");
        registry.Remove("unrelated");
        if (registry.ForOrganization(otherOrganization).Length != 0) throw new Exception("Disconnect leaked a subscription");
        Console.WriteLine("PASS role/team access loss removes canvas groups; membership loss removes every tab without affecting other organizations");
    }
}

internal sealed class TestConnection(string id) : HubCallerContext
{
    public bool Aborted { get; private set; }
    public override string ConnectionId => id;
    public override string? UserIdentifier => null;
    public override ClaimsPrincipal? User => null;
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() => Aborted = true;
}
