using System.Collections.Concurrent;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Ideas;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Realtime;

public sealed record WorkshopSubscription(HubCallerContext Context, Guid OrganizationId, Guid UserId, Guid? ProjectId)
{
    public string Group => ProjectId is { } projectId
        ? WorkshopHub.GetIdeasGroupName(OrganizationId, projectId)
        : WorkshopHub.GetGroupName(OrganizationId);
}

// Like the presence tracker, this registry is local to the API instance.
public sealed class WorkshopSubscriptions
{
    private readonly ConcurrentDictionary<(string Connection, string Group), WorkshopSubscription> _items = new();
    public void Add(WorkshopSubscription subscription) =>
        _items[(subscription.Context.ConnectionId, subscription.Group)] = subscription;
    public WorkshopSubscription[] ForOrganization(Guid id) => _items.Values.Where(s => s.OrganizationId == id).ToArray();
    public void Remove(string connectionId, string group) => _items.TryRemove((connectionId, group), out _);
    public void Remove(string connectionId)
    {
        foreach (var key in _items.Keys.Where(k => k.Connection == connectionId)) _items.TryRemove(key, out _);
    }
}

public sealed class WorkshopRealtime(
    WorkshopSubscriptions subscriptions,
    IOrganizationRepository organizations,
    IIdeasService ideas,
    WorkshopPresenceTracker presence,
    IHubContext<WorkshopHub> hub)
{
    public async Task RevokeMember(Guid organizationId, Guid userId)
    {
        if (presence.RemoveMember(organizationId, userId))
            await hub.Clients.Group(WorkshopHub.GetGroupName(organizationId)).SendAsync(
                "MemberOffline", userId.ToString(), organizationId.ToString());
        await Revalidate(organizationId);
    }

    public async Task Revalidate(Guid organizationId, Guid? canvasId = null)
    {
        var access = new Dictionary<(Guid UserId, Guid? ProjectId), bool>();
        foreach (var subscription in subscriptions.ForOrganization(organizationId))
        {
            if (canvasId is not null && subscription.ProjectId != canvasId) continue;
            var key = (subscription.UserId, subscription.ProjectId);
            if (!access.TryGetValue(key, out var allowed))
            {
                allowed = subscription.ProjectId is { } projectId
                    ? await ideas.CanView(organizationId, projectId, subscription.UserId, CancellationToken.None)
                    : await organizations.IsMemberAsync(organizationId, subscription.UserId);
                access[key] = allowed;
            }
            if (allowed) continue;
            // Do not rely on a cooperative browser to leave a group after access is revoked.
            await hub.Groups.RemoveFromGroupAsync(subscription.Context.ConnectionId, subscription.Group);
            subscriptions.Remove(subscription.Context.ConnectionId, subscription.Group);
            subscription.Context.Abort();
        }
    }

    public async Task Changed(Guid organizationId)
    {
        await Revalidate(organizationId);
        await hub.Clients.Group(WorkshopHub.GetGroupName(organizationId)).SendAsync(
            "OrganizationMembersChanged", organizationId.ToString());
    }
}
