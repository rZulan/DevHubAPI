using System.ComponentModel.DataAnnotations;
using DevHub.Application.Organizations;
using DevHub.Application.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

public sealed record SaveOrganizationRequest(
    [property: Required, StringLength(150)] string Name,
    [property: StringLength(1000)] string? Description);

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
    [ProducesResponseType<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK)]
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
}
