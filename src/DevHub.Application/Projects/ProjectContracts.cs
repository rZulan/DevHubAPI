using System.ComponentModel.DataAnnotations;
using DevHub.Application.Common;
using MediatR;

namespace DevHub.Application.Projects;

public sealed record ProjectResponse(
    Guid Id,
    Guid OrganizationId,
    Guid TeamId,
    Guid LeadUserId,
    string Name,
    string Summary,
    string Status,
    IReadOnlyList<string> TechStack,
    string? RepositoryUrl,
    string? DocumentationUrl,
    string? DesignUrl,
    string? LiveUrl,
    string? SdlcMethod,
    string? DatabaseDetails,
    IReadOnlyList<string> Environments,
    DateOnly? StartDate,
    DateOnly? TargetDate,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);

public sealed record CreateProjectCommand(
    Guid OrganizationId,
    Guid RequestingUserId,
    Guid TeamId,
    Guid LeadUserId,
    [property: Required, StringLength(150)] string Name,
    [property: Required, StringLength(2000)] string Summary,
    [property: Required, StringLength(40)] string Status,
    IReadOnlyList<string> TechStack,
    [property: StringLength(2048)] string? RepositoryUrl,
    [property: StringLength(2048)] string? DocumentationUrl,
    [property: StringLength(2048)] string? DesignUrl,
    [property: StringLength(2048)] string? LiveUrl,
    [property: StringLength(150)] string? SdlcMethod,
    [property: StringLength(500)] string? DatabaseDetails,
    IReadOnlyList<string> Environments,
    DateOnly? StartDate,
    DateOnly? TargetDate)
    : IRequest<Result<ProjectResponse>>;

public sealed record UpdateProjectCommand(
    Guid OrganizationId,
    Guid ProjectId,
    Guid RequestingUserId,
    Guid TeamId,
    Guid LeadUserId,
    [property: Required, StringLength(150)] string Name,
    [property: Required, StringLength(2000)] string Summary,
    [property: Required, StringLength(40)] string Status,
    IReadOnlyList<string> TechStack,
    [property: StringLength(2048)] string? RepositoryUrl,
    [property: StringLength(2048)] string? DocumentationUrl,
    [property: StringLength(2048)] string? DesignUrl,
    [property: StringLength(2048)] string? LiveUrl,
    [property: StringLength(150)] string? SdlcMethod,
    [property: StringLength(500)] string? DatabaseDetails,
    IReadOnlyList<string> Environments,
    DateOnly? StartDate,
    DateOnly? TargetDate)
    : IRequest<Result<ProjectResponse>>;

public sealed record DeleteProjectCommand(
    Guid OrganizationId,
    Guid ProjectId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record ListProjectsQuery(Guid OrganizationId, Guid RequestingUserId)
    : IRequest<Result<IReadOnlyList<ProjectResponse>>>;

public sealed record GetProjectQuery(
    Guid OrganizationId,
    Guid ProjectId,
    Guid RequestingUserId) : IRequest<Result<ProjectResponse>>;
