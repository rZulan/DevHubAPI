using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Organizations;
using DevHub.Domain.Organizations;
using DevHub.Domain.Projects;
using DevHub.Domain.Teams;
using MediatR;

namespace DevHub.Application.Projects;

internal static class ProjectErrors
{
    public static readonly Error NotFound = new(
        "Projects.NotFound",
        "The project was not found in this organization.",
        ErrorType.NotFound);

    public static readonly Error TeamNotFound = new(
        "Projects.TeamNotFound",
        "The selected team was not found in this organization.",
        ErrorType.NotFound);

    public static readonly Error NameAlreadyExists = new(
        "Projects.NameAlreadyExists",
        "A project with this name already exists in the organization.",
        ErrorType.Conflict);

    public static readonly Error ManagementRequired = new(
        "Projects.ManagementRequired",
        "Only the organization owner or the owning team's leader can manage this project.",
        ErrorType.Forbidden);

    public static readonly Error OwnerRequiredToMove = new(
        "Projects.OwnerRequiredToMove",
        "Only the organization owner can move a project to another team.",
        ErrorType.Forbidden);

    public static readonly Error LeadMustBelongToTeam = new(
        "Projects.LeadMustBelongToTeam",
        "The project lead must be a member of the selected team.",
        ErrorType.Conflict);

    public static Error Invalid(IReadOnlyDictionary<string, string[]> errors) => new(
        "Projects.ValidationFailed",
        "One or more project fields are invalid.",
        ErrorType.Validation,
        errors);
}

internal static class ProjectMappings
{
    public static ProjectResponse ToResponse(this Project project) => new(
        project.Id,
        project.OrganizationId,
        project.TeamId,
        project.LeadUserId,
        project.Name,
        project.Summary,
        ToDisplayStatus(project.Status),
        Project.SplitValues(project.TechStack),
        project.RepositoryUrl,
        project.DocumentationUrl,
        project.DesignUrl,
        project.LiveUrl,
        project.SdlcMethod,
        project.DatabaseDetails,
        Project.SplitValues(project.Environments),
        project.StartDate,
        project.TargetDate,
        project.CreatedAtUtc,
        project.UpdatedAtUtc);

    public static bool TryParseStatus(string status, out ProjectStatus parsedStatus) =>
        Enum.TryParse(
            status.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal),
            ignoreCase: true,
            out parsedStatus);

    private static string ToDisplayStatus(ProjectStatus status) => status switch
    {
        ProjectStatus.InDevelopment => "In development",
        ProjectStatus.OnHold => "On hold",
        _ => status.ToString()
    };
}

internal static class ProjectValidation
{
    public static IReadOnlyDictionary<string, string[]> Validate(
        object request,
        string status,
        IReadOnlyList<string> techStack,
        IReadOnlyList<string> environments,
        DateOnly? startDate,
        DateOnly? targetDate,
        params (string Name, string? Value)[] urls)
    {
        var errors = RequestValidation.Validate(request)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        if (!ProjectMappings.TryParseStatus(status, out _))
        {
            errors[nameof(status)] =
                ["Status must be Planning, In development, Review, On hold, Shipped, or Archived."];
        }

        if (Project.JoinValues(techStack).Length > 2000)
        {
            errors[nameof(techStack)] = ["The combined technology stack is too long."];
        }

        if (Project.JoinValues(environments).Length > 1000)
        {
            errors[nameof(environments)] = ["The combined environment list is too long."];
        }

        foreach (var (name, value) in urls)
        {
            if (!string.IsNullOrWhiteSpace(value) &&
                (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            {
                errors[name] = ["Enter a complete http or https URL."];
            }
        }

        if (startDate.HasValue && targetDate.HasValue && targetDate < startDate)
        {
            errors[nameof(targetDate)] = ["The target date cannot be earlier than the start date."];
        }

        return errors;
    }
}

internal sealed class CreateProjectCommandHandler(
    IOrganizationRepository organizationRepository,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CreateProjectCommand, Result<ProjectResponse>>
{
    public async Task<Result<ProjectResponse>> Handle(
        CreateProjectCommand request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.Invalid(errors));
        }

        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<ProjectResponse>.Failure(OrganizationErrors.NotFound);
        }

        var team = organization.Teams.SingleOrDefault(candidate => candidate.Id == request.TeamId);
        if (team is null)
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.TeamNotFound);
        }

        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageProjects) &&
            !team.IsLeader(request.RequestingUserId))
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.ManagementRequired);
        }

        if (!team.HasMember(request.LeadUserId))
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.LeadMustBelongToTeam);
        }

        if (await projectRepository.NameExistsAsync(
                organization.Id,
                request.Name,
                cancellationToken: cancellationToken))
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.NameAlreadyExists);
        }

        ProjectMappings.TryParseStatus(request.Status, out var status);
        var project = Project.Create(
            organization.Id,
            team.Id,
            request.LeadUserId,
            request.Name,
            request.Summary,
            status,
            request.TechStack,
            request.RepositoryUrl,
            request.DocumentationUrl,
            request.DesignUrl,
            request.LiveUrl,
            request.SdlcMethod,
            request.DatabaseDetails,
            request.Environments,
            request.StartDate,
            request.TargetDate,
            timeProvider.GetUtcNow());
        projectRepository.Add(project);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.NameAlreadyExists);
        }

        return Result<ProjectResponse>.Success(project.ToResponse());
    }

    private static IReadOnlyDictionary<string, string[]> Validate(CreateProjectCommand request) =>
        ProjectValidation.Validate(
            request,
            request.Status,
            request.TechStack,
            request.Environments,
            request.StartDate,
            request.TargetDate,
            (nameof(request.RepositoryUrl), request.RepositoryUrl),
            (nameof(request.DocumentationUrl), request.DocumentationUrl),
            (nameof(request.DesignUrl), request.DesignUrl),
            (nameof(request.LiveUrl), request.LiveUrl));
}

internal sealed class UpdateProjectCommandHandler(
    IOrganizationRepository organizationRepository,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateProjectCommand, Result<ProjectResponse>>
{
    public async Task<Result<ProjectResponse>> Handle(
        UpdateProjectCommand request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.Invalid(errors));
        }

        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<ProjectResponse>.Failure(OrganizationErrors.NotFound);
        }

        var project = await projectRepository.GetByIdAsync(
            organization.Id,
            request.ProjectId,
            cancellationToken);
        if (project is null)
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.NotFound);
        }

        var currentTeam = organization.Teams.Single(team => team.Id == project.TeamId);
        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageProjects) &&
            !currentTeam.IsLeader(request.RequestingUserId))
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.ManagementRequired);
        }

        if (project.TeamId != request.TeamId &&
            !organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageProjects))
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.OwnerRequiredToMove);
        }

        var team = organization.Teams.SingleOrDefault(candidate => candidate.Id == request.TeamId);
        if (team is null)
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.TeamNotFound);
        }

        if (!team.HasMember(request.LeadUserId))
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.LeadMustBelongToTeam);
        }

        if (await projectRepository.NameExistsAsync(
                organization.Id,
                request.Name,
                project.Id,
                cancellationToken))
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.NameAlreadyExists);
        }

        ProjectMappings.TryParseStatus(request.Status, out var status);
        project.Update(
            team.Id,
            request.LeadUserId,
            request.Name,
            request.Summary,
            status,
            request.TechStack,
            request.RepositoryUrl,
            request.DocumentationUrl,
            request.DesignUrl,
            request.LiveUrl,
            request.SdlcMethod,
            request.DatabaseDetails,
            request.Environments,
            request.StartDate,
            request.TargetDate,
            timeProvider.GetUtcNow());

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.NameAlreadyExists);
        }

        return Result<ProjectResponse>.Success(project.ToResponse());
    }

    private static IReadOnlyDictionary<string, string[]> Validate(UpdateProjectCommand request) =>
        ProjectValidation.Validate(
            request,
            request.Status,
            request.TechStack,
            request.Environments,
            request.StartDate,
            request.TargetDate,
            (nameof(request.RepositoryUrl), request.RepositoryUrl),
            (nameof(request.DocumentationUrl), request.DocumentationUrl),
            (nameof(request.DesignUrl), request.DesignUrl),
            (nameof(request.LiveUrl), request.LiveUrl));
}

internal sealed class DeleteProjectCommandHandler(
    IOrganizationRepository organizationRepository,
    IProjectRepository projectRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteProjectCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        DeleteProjectCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }

        var project = await projectRepository.GetByIdAsync(
            organization.Id,
            request.ProjectId,
            cancellationToken);
        if (project is null)
        {
            return Result<Unit>.Failure(ProjectErrors.NotFound);
        }

        var team = organization.Teams.Single(candidate => candidate.Id == project.TeamId);
        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageProjects) &&
            !team.IsLeader(request.RequestingUserId))
        {
            return Result<Unit>.Failure(ProjectErrors.ManagementRequired);
        }

        projectRepository.Remove(project);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class ListProjectsQueryHandler(
    IOrganizationRepository organizationRepository,
    IProjectRepository projectRepository)
    : IRequestHandler<ListProjectsQuery, Result<IReadOnlyList<ProjectResponse>>>
{
    public async Task<Result<IReadOnlyList<ProjectResponse>>> Handle(
        ListProjectsQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<IReadOnlyList<ProjectResponse>>.Failure(OrganizationErrors.NotFound);
        }

        var projects = await projectRepository.ListVisibleAsync(
            organization.Id,
            request.RequestingUserId,
            organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ViewAllProjects),
            cancellationToken);
        return Result<IReadOnlyList<ProjectResponse>>.Success(
            projects.Select(project => project.ToResponse()).ToArray());
    }
}

internal sealed class GetProjectQueryHandler(
    IOrganizationRepository organizationRepository,
    IProjectRepository projectRepository)
    : IRequestHandler<GetProjectQuery, Result<ProjectResponse>>
{
    public async Task<Result<ProjectResponse>> Handle(
        GetProjectQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<ProjectResponse>.Failure(OrganizationErrors.NotFound);
        }

        var project = await projectRepository.GetByIdAsync(
            organization.Id,
            request.ProjectId,
            cancellationToken);
        if (project is null)
        {
            return Result<ProjectResponse>.Failure(ProjectErrors.NotFound);
        }

        var team = organization.Teams.Single(candidate => candidate.Id == project.TeamId);
        var canView = organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ViewAllProjects) ||
                      team.HasMember(request.RequestingUserId);
        return canView
            ? Result<ProjectResponse>.Success(project.ToResponse())
            : Result<ProjectResponse>.Failure(ProjectErrors.NotFound);
    }
}
