using System.ComponentModel.DataAnnotations;
using DevHub.Application.Organizations;
using DevHub.Application.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

public sealed record SaveOrganizationRequest(
    [Required, StringLength(150)] string Name,
    [StringLength(1000)] string? Description);

public sealed record SaveOrganizationRoleRequest(
    [Required, StringLength(100)] string Name,
    [Required, StringLength(20)] string Color,
    int Position,
    IReadOnlyList<string> Permissions);

/// <summary>Creates and manages organizations and organization membership.</summary>
[Tags("Organizations")]
[Route("api/organizations")]
[Authorize]
public sealed class OrganizationsController(ISender sender) : ApiControllerBase
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
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
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
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
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
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpPost("{organizationId:guid}/invites")]
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

    [HttpPost("invites/{token}/accept")]
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
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
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
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpPut("{organizationId:guid}/owners/{memberUserId:guid}")]
    [EndpointName("PromoteOrganizationOwner")]
    public async Task<IActionResult> PromoteOwner(
        Guid organizationId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new PromoteOrganizationOwnerCommand(
            organizationId, memberUserId, userId), cancellationToken);
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
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }
}
