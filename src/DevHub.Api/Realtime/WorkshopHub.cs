using System.IdentityModel.Tokens.Jwt;
using DevHub.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Realtime;

[Authorize]
public sealed class WorkshopHub(
    IOrganizationRepository organizationRepository,
    WorkshopPresenceTracker presenceTracker) : Hub
{
    public async Task<IReadOnlyList<WorkshopPresence>> JoinOrganization(Guid organizationId, string status)
    {
        var cancellationToken = Context.ConnectionAborted;
        var userId = GetAuthenticatedUserId();
        if (!await organizationRepository.IsMemberAsync(organizationId, userId, cancellationToken))
            throw new HubException("You are not a member of this organization.");

        var groupName = GetGroupName(organizationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName, cancellationToken);
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

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        foreach (var result in presenceTracker.Leave(Context.ConnectionId).Where(result => result.IsNowOffline))
            await Clients.Group(GetGroupName(result.OrganizationId)).SendAsync(
                "MemberOffline", result.UserId.ToString(), result.OrganizationId.ToString());
        await base.OnDisconnectedAsync(exception);
    }

    private Guid GetAuthenticatedUserId()
    {
        var subject = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out var userId) ? userId
            : throw new HubException("The authenticated user is invalid.");
    }
    public static string GetGroupName(Guid organizationId) => $"workshop:{organizationId:N}";
}
