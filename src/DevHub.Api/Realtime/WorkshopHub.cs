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
    public async Task<WorkshopPresence[]> JoinOrganization(
        Guid organizationId,
        string status,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        var organization = await organizationRepository.GetByIdAsync(
            organizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(userId))
        {
            throw new HubException("You are not a member of this organization.");
        }

        var groupName = GetGroupName(organizationId);
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName, cancellationToken);
        var result = presenceTracker.Join(organizationId, userId, Context.ConnectionId, status);

        if (result.IsFirstConnection)
        {
            var normalizedStatus = WorkshopPresenceTracker.NormalizeStatus(status);
            await Clients.OthersInGroup(groupName).SendAsync(
                "MemberStatusChanged",
                userId.ToString(),
                normalizedStatus == "invisible" ? "offline" : normalizedStatus,
                cancellationToken);
        }

        return result.Members.ToArray();
    }

    public async Task SetStatus(
        Guid organizationId,
        string status,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();
        var organization = await organizationRepository.GetByIdAsync(
            organizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(userId))
        {
            throw new HubException("You are not a member of this organization.");
        }

        var normalizedStatus = presenceTracker.SetStatus(organizationId, userId, status);
        await Clients.OthersInGroup(GetGroupName(organizationId)).SendAsync(
            "MemberStatusChanged",
            userId.ToString(),
            normalizedStatus == "invisible" ? "offline" : normalizedStatus,
            cancellationToken);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var result = presenceTracker.Leave(Context.ConnectionId);
        if (result is { IsNowOffline: true })
        {
            await Clients.Group(GetGroupName(result.OrganizationId)).SendAsync(
                "MemberOffline",
                result.UserId.ToString());
        }

        await base.OnDisconnectedAsync(exception);
    }

    private Guid GetAuthenticatedUserId()
    {
        var subject = Context.User?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        return Guid.TryParse(subject, out var userId)
            ? userId
            : throw new HubException("The authenticated user is invalid.");
    }

    private static string GetGroupName(Guid organizationId) =>
        $"workshop:{organizationId:N}";
}
