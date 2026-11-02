using System.ComponentModel.DataAnnotations;
using DevHub.Application.Organizations;
using DevHub.Application.Users;
using DevHub.Api.Realtime;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Controllers;

public sealed record SaveOrganizationRequest(
    [Required, StringLength(150)] string Name,
    [StringLength(1000)] string? Description);

public sealed record SaveAvatarRequest([DevHub.Application.Common.AvatarImage] string? AvatarUrl);

public sealed record SaveOrganizationRoleRequest(
    [Required, StringLength(100)] string Name,
    [Required, StringLength(20)] string Color,
    int Position,
    [Required] IReadOnlyList<string> Permissions) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(Color ?? "", "^#[0-9a-fA-F]{6}$")) yield return new("Choose a six-digit hex role color.", [nameof(Color)]);
        if (Permissions is not null && (Permissions.Count > DevHub.Domain.Organizations.OrganizationPermissions.All.Count || Permissions.Distinct().Count() != Permissions.Count || Permissions.Any(permission => !DevHub.Domain.Organizations.OrganizationPermissions.All.Contains(permission))))
            yield return new("Choose unique permissions from the supported organization permissions.", [nameof(Permissions)]);
    }
}

public sealed record MoveOrganizationRoleRequest([Range(-1, 1)] int Direction);

public sealed record SaveOrganizationDashboardRequest(
    [Required] IReadOnlyList<DashboardWidgetDefinition> Widgets);

public sealed record ChangeOrganizationMemberNicknameRequest([StringLength(80)] string? Nickname);

/// <summary>Creates and manages organizations and organization membership.</summary>
[Tags("Organizations")]
[Route("api/organizations")]
[Authorize]
public sealed class OrganizationsController(
    ISender sender,
    IHubContext<WorkshopHub> workshopHub,
    WorkshopRealtime realtime) : ApiControllerBase
{
    [HttpGet]
    [EndpointName("ListOrganizations")]
    [ProducesResponseType<IReadOnlyList<OrganizationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new ListOrganizationsQuery(userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpGet("{organizationId:guid}")]
    [EndpointName("GetOrganization")]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new GetOrganizationQuery(organizationId, userId),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("resource-create")]
    [EndpointName("CreateOrganization")]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        [FromBody] SaveOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new CreateOrganizationCommand(userId, request.Name, request.Description),
            cancellationToken);
        if (result.IsSuccess)
            await workshopHub.Clients.User(userId.ToString()).SendAsync(
                "OrganizationMembershipChanged", result.Value!.Id.ToString());
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { organizationId = result.Value!.Id }, result.Value)
            : Failure(result.Error!);
    }

    [HttpPut("{organizationId:guid}")]
    [EndpointName("UpdateOrganization")]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid organizationId,
        [FromBody] SaveOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new UpdateOrganizationCommand(
                organizationId,
                userId,
                request.Name,
                request.Description),
            cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut("{organizationId:guid}/avatar")]
    [EndpointName("SetOrganizationAvatar")]
    [RequestSizeLimit(200000)]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetAvatar(Guid organizationId, [FromBody] SaveAvatarRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new SetOrganizationAvatarCommand(organizationId, userId, request.AvatarUrl), cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpDelete("{organizationId:guid}")]
    [EndpointName("DeleteOrganization")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new DeleteOrganizationCommand(organizationId, userId),
            cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await workshopHub.Clients.Group(WorkshopHub.GetGroupName(organizationId)).SendAsync(
            "OrganizationAccessRevoked", organizationId.ToString());
        await realtime.Revalidate(organizationId);
        return NoContent();
    }

    [HttpGet("{organizationId:guid}/members")]
    [EndpointName("ListOrganizationMembers")]
    [ProducesResponseType<IReadOnlyList<OrganizationMemberResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMembers(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new ListOrganizationMembersQuery(organizationId, userId),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut("{organizationId:guid}/members/{memberUserId:guid}")]
    [EndpointName("AddOrganizationMember")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AddMember(
        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new AddOrganizationMemberCommand(organizationId, memberUserId, userId),
            cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await NotifyMembershipChanged(organizationId, memberUserId);
        return NoContent();
    }

    [HttpDelete("{organizationId:guid}/members/{memberUserId:guid}")]
    [EndpointName("RemoveOrganizationMember")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(
        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new RemoveOrganizationMemberCommand(organizationId, memberUserId, userId),
            cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await workshopHub.Clients.User(memberUserId.ToString()).SendAsync(
            "OrganizationAccessRevoked", organizationId.ToString());
        await realtime.RevokeMember(organizationId, memberUserId);
        await NotifyMembershipChanged(organizationId, memberUserId);
        return NoContent();
    }

    [HttpPatch("{organizationId:guid}/members/{memberUserId:guid}/nickname")]
    [EndpointName("ChangeOrganizationMemberNickname")]
    public async Task<IActionResult> ChangeMemberNickname(
        Guid organizationId,
        Guid memberUserId,
        [FromBody] ChangeOrganizationMemberNicknameRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new ChangeOrganizationMemberNicknameCommand(
            organizationId, memberUserId, userId, request.Nickname), cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpPost("{organizationId:guid}/invites")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("invite-action")]
    [EndpointName("CreateOrganizationInvite")]
    [ProducesResponseType<OrganizationInviteResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateInvite(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new CreateOrganizationInviteCommand(organizationId, userId),
            cancellationToken);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Failure(result.Error!);
    }

    [HttpGet("{organizationId:guid}/invites")]
    [EndpointName("ListOrganizationInvites")]
    public async Task<IActionResult> ListInvites(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new ListOrganizationInvitesQuery(organizationId, userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpDelete("{organizationId:guid}/invites/{inviteId:guid}")]
    [EndpointName("RevokeOrganizationInvite")]
    public async Task<IActionResult> RevokeInvite(Guid organizationId, Guid inviteId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new RevokeOrganizationInviteCommand(organizationId, inviteId, userId), cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpPost("invites/{token}/accept")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("invite-action")]
    [EndpointName("AcceptOrganizationInvite")]
    [ProducesResponseType<OrganizationResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AcceptInvite(
        string token,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new AcceptOrganizationInviteCommand(token, userId),
            cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await NotifyMembershipChanged(result.Value!.Id, userId);
        return Ok(result.Value);
    }

    [HttpGet("{organizationId:guid}/dashboard")]
    [EndpointName("GetOrganizationDashboard")]
    [ProducesResponseType<OrganizationDashboardResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new GetOrganizationDashboardQuery(organizationId, userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut("{organizationId:guid}/dashboard")]
    [EndpointName("PublishOrganizationDashboard")]
    [ProducesResponseType<OrganizationDashboardResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishDashboard(
        Guid organizationId,
        [FromBody] SaveOrganizationDashboardRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new PublishOrganizationDashboardCommand(
            organizationId, userId, request.Widgets), cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);

        await workshopHub.Clients.Group(WorkshopHub.GetGroupName(organizationId)).SendAsync(
            "DashboardPublished", organizationId.ToString(), cancellationToken);
        return Ok(result.Value);
    }

    [HttpGet("{organizationId:guid}/dashboard/personal")]
    [EndpointName("GetPersonalDashboard")]
    [ProducesResponseType<PersonalDashboardResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPersonalDashboard(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new GetPersonalDashboardQuery(organizationId, userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut("{organizationId:guid}/dashboard/personal")]
    [EndpointName("SavePersonalDashboard")]
    [ProducesResponseType<PersonalDashboardResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SavePersonalDashboard(Guid organizationId, [FromBody] SaveOrganizationDashboardRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new SavePersonalDashboardCommand(organizationId, userId, request.Widgets), cancellationToken);
        // Personal layouts are never broadcast to other organization members.
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpGet("{organizationId:guid}/roles")]
    [EndpointName("ListOrganizationRoles")]
    [ProducesResponseType<IReadOnlyList<OrganizationRoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRoles(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new ListOrganizationRolesQuery(organizationId, userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost("{organizationId:guid}/roles")]
    [EndpointName("CreateOrganizationRole")]
    public async Task<IActionResult> CreateRole(
        Guid organizationId,
        [FromBody] SaveOrganizationRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new CreateOrganizationRoleCommand(
            organizationId, userId, request.Name, request.Color, request.Permissions),
            cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : Failure(result.Error!);
    }

    [HttpPut("{organizationId:guid}/roles/{roleId:guid}")]
    [EndpointName("UpdateOrganizationRole")]
    public async Task<IActionResult> UpdateRole(
        Guid organizationId,
        Guid roleId,
        [FromBody] SaveOrganizationRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new UpdateOrganizationRoleCommand(
            organizationId, roleId, userId, request.Name, request.Color,
            request.Position, request.Permissions), cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost("{organizationId:guid}/roles/{roleId:guid}/move")]
    [EndpointName("MoveOrganizationRole")]
    public async Task<IActionResult> MoveRole(Guid organizationId, Guid roleId, [FromBody] MoveOrganizationRoleRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new MoveOrganizationRoleCommand(organizationId, roleId, userId, request.Direction), cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpDelete("{organizationId:guid}/roles/{roleId:guid}")]
    [EndpointName("DeleteOrganizationRole")]
    public async Task<IActionResult> DeleteRole(
        Guid organizationId,
        Guid roleId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new DeleteOrganizationRoleCommand(organizationId, roleId, userId),
            cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpPut("{organizationId:guid}/roles/{roleId:guid}/members/{memberUserId:guid}")]
    [EndpointName("AssignOrganizationRole")]
    public async Task<IActionResult> AssignRole(
        Guid organizationId,
        Guid roleId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new AssignOrganizationRoleCommand(
            organizationId, roleId, memberUserId, userId), cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpDelete("{organizationId:guid}/roles/{roleId:guid}/members/{memberUserId:guid}")]
    [EndpointName("RemoveOrganizationRole")]
    public async Task<IActionResult> RemoveRole(
        Guid organizationId,
        Guid roleId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new RemoveOrganizationRoleCommand(
            organizationId, roleId, memberUserId, userId), cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpPut("{organizationId:guid}/owners/{memberUserId:guid}")]
    [HttpPut("{organizationId:guid}/ownership/{memberUserId:guid}", Name = "TransferOrganizationOwnership")]
    public async Task<IActionResult> TransferOwnership(
        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new TransferOrganizationOwnershipCommand(
            organizationId, memberUserId, userId), cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpDelete("{organizationId:guid}/members/me")]
    [EndpointName("LeaveOrganization")]
    public async Task<IActionResult> Leave(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new LeaveOrganizationCommand(organizationId, userId), cancellationToken);
        if (!result.IsSuccess) return Failure(result.Error!);
        await workshopHub.Clients.User(userId.ToString()).SendAsync(
            "OrganizationAccessRevoked", organizationId.ToString());
        await realtime.RevokeMember(organizationId, userId);
        await NotifyMembershipChanged(organizationId, userId);
        return NoContent();
    }

    private async Task NotifyMembershipChanged(Guid organizationId, Guid memberUserId)
    {
        // The write is already committed: a closing request must not cancel the notification.
        await workshopHub.Clients.Group(WorkshopHub.GetGroupName(organizationId)).SendAsync(
            "OrganizationMembersChanged", organizationId.ToString());
        // Also refresh the affected user's other tabs, including tabs outside this organization.
        await workshopHub.Clients.User(memberUserId.ToString()).SendAsync(
            "OrganizationMembershipChanged", organizationId.ToString());
    }
}
