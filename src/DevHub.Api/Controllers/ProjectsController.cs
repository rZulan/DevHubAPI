using DevHub.Api.Realtime;
using System.ComponentModel.DataAnnotations;
using DevHub.Application.Projects;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

public sealed record SaveProjectRequest(
    [Required, StringLength(150)] string Name,
    [Required, StringLength(2000)] string Summary,
    [Required, StringLength(40)] string Status,
    Guid TeamId,
    Guid LeadUserId,
    IReadOnlyList<string>? TechStack,
    [Url, StringLength(2048)] string? RepositoryUrl,
    [Url, StringLength(2048)] string? DocumentationUrl,
    [Url, StringLength(2048)] string? DesignUrl,
    [Url, StringLength(2048)] string? LiveUrl,
    [StringLength(150)] string? SdlcMethod,
    [StringLength(500)] string? DatabaseDetails,
    IReadOnlyList<string>? Environments,
    DateOnly? StartDate,
    DateOnly? TargetDate);

/// <summary>Creates and manages projects within an organization.</summary>
[Tags("Projects")]
[Route("api/organizations/{organizationId:guid}/projects")]
[Authorize]
public sealed class ProjectsController(ISender sender, WorkshopRealtime realtime) : ApiControllerBase
{
    [HttpGet]
    [EndpointName("ListProjects")]
    [ProducesResponseType<IReadOnlyList<ProjectResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new ListProjectsQuery(organizationId, userId),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpGet("{projectId:guid}")]
    [EndpointName("GetProject")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(
        Guid organizationId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new GetProjectQuery(organizationId, projectId, userId),
            cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost]
    [EndpointName("CreateProject")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create(
        Guid organizationId,
        [FromBody] SaveProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            CreateCommand(organizationId, userId, request),
            cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess
            ? CreatedAtAction(
                nameof(Get),
                new { organizationId, projectId = result.Value!.Id },
                result.Value)
            : Failure(result.Error!);
    }

    [HttpPut("{projectId:guid}")]
    [EndpointName("UpdateProject")]
    [ProducesResponseType<ProjectResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid organizationId,
        Guid projectId,
        [FromBody] SaveProjectRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            UpdateCommand(organizationId, projectId, userId, request),
            cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpDelete("{projectId:guid}")]
    [EndpointName("DeleteProject")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        Guid organizationId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await sender.Send(
            new DeleteProjectCommand(organizationId, projectId, userId),
            cancellationToken);
        if (result.IsSuccess) await realtime.Changed(organizationId);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    private static CreateProjectCommand CreateCommand(
        Guid organizationId,
        Guid userId,
        SaveProjectRequest request) => new(
        organizationId,
        userId,
        request.TeamId,
        request.LeadUserId,
        request.Name,
        request.Summary,
        request.Status,
        request.TechStack ?? [],
        request.RepositoryUrl,
        request.DocumentationUrl,
        request.DesignUrl,
        request.LiveUrl,
        request.SdlcMethod,
        request.DatabaseDetails,
        request.Environments ?? [],
        request.StartDate,
        request.TargetDate);

    private static UpdateProjectCommand UpdateCommand(
        Guid organizationId,
        Guid projectId,
        Guid userId,
        SaveProjectRequest request) => new(
        organizationId,
        projectId,
        userId,
        request.TeamId,
        request.LeadUserId,
        request.Name,
        request.Summary,
        request.Status,
        request.TechStack ?? [],
        request.RepositoryUrl,
        request.DocumentationUrl,
        request.DesignUrl,
        request.LiveUrl,
        request.SdlcMethod,
        request.DatabaseDetails,
        request.Environments ?? [],
        request.StartDate,
        request.TargetDate);
}
