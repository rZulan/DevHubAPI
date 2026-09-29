using System.IdentityModel.Tokens.Jwt;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Ideas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Realtime;

[Authorize]
public sealed class WorkshopHub(
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IIdeasService ideasService,
    WorkshopPresenceTracker presenceTracker,
    IdeasSelectionTracker ideasSelectionTracker,
    WorkshopSubscriptions subscriptions,
    WorkshopRealtime realtime) : Hub
{
    public async Task<IReadOnlyList<WorkshopPresence>> JoinOrganization(Guid organizationId, string status)
    {
        var cancellationToken = Context.ConnectionAborted;
        var userId = GetAuthenticatedUserId();
        if (!await organizationRepository.IsMemberAsync(organizationId, userId, cancellationToken))
            throw new HubException("You are not a member of this organization.");

        var groupName = GetGroupName(organizationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName, cancellationToken);
        subscriptions.Add(new(Context, organizationId, userId, null));
        // Close the window where membership changed while the connection was joining.
        if (!await organizationRepository.IsMemberAsync(organizationId, userId, cancellationToken))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName, cancellationToken);
            Context.Abort();
            throw new HubException("You are not a member of this organization.");
        }
        var result = presenceTracker.Join(organizationId, userId, Context.ConnectionId, status);
        if (result.StatusChanged)
            await Clients.OthersInGroup(groupName).SendAsync("MemberStatusChanged",
                userId.ToString(), result.Status, organizationId.ToString(), cancellationToken);
        return result.Members;
    }

    public async Task SetStatus(Guid organizationId, string status)
    {
        var cancellationToken = Context.ConnectionAborted;
        var userId = GetAuthenticatedUserId();
        if (!await organizationRepository.IsMemberAsync(organizationId, userId, cancellationToken))
            throw new HubException("You are not a member of this organization.");

        var changedStatus = presenceTracker.SetStatus(organizationId, userId, Context.ConnectionId, status);
        if (changedStatus is not null)
            await Clients.OthersInGroup(GetGroupName(organizationId)).SendAsync("MemberStatusChanged",
                userId.ToString(), changedStatus, organizationId.ToString(), cancellationToken);
    }

    public async Task<IReadOnlyList<IdeasSelection>> JoinIdeas(Guid organizationId, Guid projectId)
    {
        var cancellationToken = Context.ConnectionAborted;
        var userId = GetAuthenticatedUserId();
        var allowed = await ideasService.CanView(organizationId, projectId, userId, cancellationToken);
        if (!allowed) throw new HubException("You cannot access this Ideas canvas.");
        var user = await userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new HubException("The authenticated user no longer exists.");
        var groupName = GetIdeasGroupName(organizationId, projectId);
        if (Context.Items.TryGetValue("ideas-group", out var oldGroup) && oldGroup is string previousGroup)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, previousGroup, cancellationToken);
            subscriptions.Remove(Context.ConnectionId, previousGroup);
            await LeaveIdeas();
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName, cancellationToken);
        subscriptions.Add(new(Context, organizationId, userId, projectId));
        if (!await ideasService.CanView(organizationId, projectId, userId, cancellationToken))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName, cancellationToken);
            Context.Abort();
            throw new HubException("You cannot access this Ideas canvas.");
        }
        Context.Items["ideas-group"] = groupName;
        Context.Items["ideas-organization"] = organizationId;
        Context.Items["ideas-project"] = projectId.ToString();
        return ideasSelectionTracker.Join(organizationId, projectId, userId, Context.ConnectionId, user.Username);
    }

    public async Task SetIdeasSelection(IReadOnlyList<string>? shapeIds)
    {
        if (!Context.Items.TryGetValue("ideas-organization", out var organizationValue) || organizationValue is not Guid organizationId ||
            !Context.Items.TryGetValue("ideas-project", out var projectValue) || projectValue is not string projectId)
            throw new HubException("Join the Ideas canvas before publishing a selection.");
        if (!await ideasService.CanView(organizationId, Guid.Parse(projectId), GetAuthenticatedUserId(), Context.ConnectionAborted))
        {
            await realtime.Revalidate(organizationId);
            throw new HubException("You cannot access this Ideas canvas.");
        }
        var selection = ideasSelectionTracker.Update(Context.ConnectionId,
            (shapeIds ?? []).Where(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 128).ToArray());
        if (selection is null || !Context.Items.TryGetValue("ideas-group", out var value) || value is not string groupName)
            throw new HubException("Join the Ideas canvas before publishing a selection.");
        await Clients.OthersInGroup(groupName).SendAsync("IdeasSelectionChanged", projectId,
            selection.UserId.ToString(), selection.Username, selection.ShapeIds, Context.ConnectionAborted);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        subscriptions.Remove(Context.ConnectionId);
        await LeaveIdeas();
        foreach (var result in presenceTracker.Leave(Context.ConnectionId).Where(result => result.IsNowOffline))
            await Clients.Group(GetGroupName(result.OrganizationId)).SendAsync(
                "MemberOffline", result.UserId.ToString(), result.OrganizationId.ToString());
        await base.OnDisconnectedAsync(exception);
    }

    private async Task LeaveIdeas()
    {
        if (ideasSelectionTracker.Leave(Context.ConnectionId) is { } selection)
            await Clients.Group(GetIdeasGroupName(selection.OrganizationId, selection.ProjectId)).SendAsync(
                "IdeasSelectionChanged", selection.ProjectId.ToString(), selection.Selection.UserId.ToString(),
                selection.Selection.Username, selection.Selection.ShapeIds);
    }

    private Guid GetAuthenticatedUserId()
    {
        var subject = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out var userId) ? userId
            : throw new HubException("The authenticated user is invalid.");
    }
    public static string GetGroupName(Guid organizationId) => $"workshop:{organizationId:N}";
    public static string GetIdeasGroupName(Guid organizationId, Guid projectId) => $"ideas:{organizationId:N}:{projectId:N}";
}
