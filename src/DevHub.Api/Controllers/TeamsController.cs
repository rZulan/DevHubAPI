using System.ComponentModel.DataAnnotations;
using DevHub.Application.Teams;
using DevHub.Application.Users;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

public sealed record SaveTeamRequest(
    [property: Required, StringLength(150)] string Name,
    [property: StringLength(1000)] string? Description);

/// <summary>Creates and manages teams within an organization.</summary>
[Tags("Teams")]
[Route("api/organizations/{organizationId:guid}/teams")]
[Authorize]
public sealed class TeamsController(ISender sender) : ApiControllerBase
{
    [HttpGet]
    [EndpointName("ListTeams")]
    [ProducesResponseType<IReadOnlyList<TeamResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid organizationId, CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(new ListTeamsQuery(organizationId, userId), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpGet("{teamId:guid}")]
    [EndpointName("GetTeam")]
    [ProducesResponseType<TeamResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid organizationId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new GetTeamQuery(organizationId, teamId, userId),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost]
    [EndpointName("CreateTeam")]
    [ProducesResponseType<TeamResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid organizationId,
        [FromBody] SaveTeamRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new CreateTeamCommand(organizationId, userId, request.Name, request.Description),
            cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(
                nameof(Get),
                new { organizationId, teamId = result.Value!.Id },
                result.Value)
            : Failure(result.Error!);
    }

    [HttpPut("{teamId:guid}")]
    [EndpointName("UpdateTeam")]
    [ProducesResponseType<TeamResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid organizationId,
        Guid teamId,
        [FromBody] SaveTeamRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new UpdateTeamCommand(
                organizationId,
                teamId,
                userId,
                request.Name,
                request.Description),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpDelete("{teamId:guid}")]
    [EndpointName("DeleteTeam")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid organizationId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new DeleteTeamCommand(organizationId, teamId, userId),
            cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpGet("{teamId:guid}/members")]
    [EndpointName("ListTeamMembers")]
    [ProducesResponseType<IReadOnlyList<UserResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ListMembers(
        Guid organizationId,
        Guid teamId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new ListTeamMembersQuery(organizationId, teamId, userId),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut("{teamId:guid}/members/{memberUserId:guid}")]
    [EndpointName("AddTeamMember")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMember(
        Guid organizationId,
        Guid teamId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new AddTeamMemberCommand(organizationId, teamId, memberUserId, userId),
            cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    [HttpDelete("{teamId:guid}/members/{memberUserId:guid}")]
    [EndpointName("RemoveTeamMember")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RemoveMember(
        Guid organizationId,
        Guid teamId,
        Guid memberUserId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new RemoveTeamMemberCommand(organizationId, teamId, memberUserId, userId),
            cancellationToken);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }
}
