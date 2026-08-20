using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Organizations;
using DevHub.Application.Users;
using DevHub.Domain.Teams;
using MediatR;

namespace DevHub.Application.Teams;

internal static class TeamErrors
{
    public static readonly Error NotFound = new(
        "Teams.NotFound",
        "The team was not found in this organization.",
        ErrorType.NotFound);

    public static readonly Error NameAlreadyExists = new(
        "Teams.NameAlreadyExists",
        "A team with this name already exists in the organization.",
        ErrorType.Conflict);

    public static readonly Error UserMustBelongToOrganization = new(
        "Teams.UserMustBelongToOrganization",
        "A user must be an organization member before joining one of its teams.",
        ErrorType.Conflict);

    public static readonly Error OwnerRequired = new(
        "Teams.OwnerRequired",
        "Only the organization owner can create, delete, or reassign leadership of a team.",
        ErrorType.Forbidden);

    public static readonly Error TeamManagementRequired = new(
        "Teams.ManagementRequired",
        "Only the organization owner or this team's leader can manage the team.",
        ErrorType.Forbidden);

    public static readonly Error LeaderCannotBeRemoved = new(
        "Teams.LeaderCannotBeRemoved",
        "The team leader cannot be removed. Assign a different leader first.",
        ErrorType.Conflict);

    public static Error Invalid(IReadOnlyDictionary<string, string[]> errors) => new(
        "Teams.ValidationFailed",
        "One or more team fields are invalid.",
        ErrorType.Validation,
        errors);
}

internal static class TeamMappings
{
    public static TeamResponse ToResponse(this Team team) => new(
        team.Id,
        team.OrganizationId,
        team.LeaderUserId,
        team.Name,
        team.Description,
        team.CreatedAtUtc,
        team.UpdatedAtUtc,
        team.Members.Count);
}

internal sealed class CreateTeamCommandHandler(
    IOrganizationRepository organizationRepository,
    ITeamRepository teamRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CreateTeamCommand, Result<TeamResponse>>
{
    public async Task<Result<TeamResponse>> Handle(
        CreateTeamCommand request,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Result<TeamResponse>.Failure(TeamErrors.Invalid(errors));
        }

        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<TeamResponse>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.IsOwner(request.RequestingUserId))
        {
            return Result<TeamResponse>.Failure(TeamErrors.OwnerRequired);
        }

        if (!organization.HasMember(request.LeaderUserId))
        {
            return Result<TeamResponse>.Failure(TeamErrors.UserMustBelongToOrganization);
        }

        if (await teamRepository.NameExistsAsync(
                organization.Id,
                request.Name,
                cancellationToken: cancellationToken))
        {
            return Result<TeamResponse>.Failure(TeamErrors.NameAlreadyExists);
        }

        var team = Team.Create(
            organization.Id,
            request.LeaderUserId,
            request.Name,
            request.Description,
            timeProvider.GetUtcNow());
        teamRepository.Add(team);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<TeamResponse>.Failure(TeamErrors.NameAlreadyExists);
        }

        return Result<TeamResponse>.Success(team.ToResponse());
    }
}

internal sealed class UpdateTeamCommandHandler(
    IOrganizationRepository organizationRepository,
    ITeamRepository teamRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateTeamCommand, Result<TeamResponse>>
{
    public async Task<Result<TeamResponse>> Handle(
        UpdateTeamCommand request,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Result<TeamResponse>.Failure(TeamErrors.Invalid(errors));
        }

        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<TeamResponse>.Failure(OrganizationErrors.NotFound);
        }

        var team = await teamRepository.GetByIdAsync(
            organization.Id,
            request.TeamId,
            cancellationToken);
        if (team is null)
        {
            return Result<TeamResponse>.Failure(TeamErrors.NotFound);
        }

        if (!organization.IsOwner(request.RequestingUserId) &&
            !team.IsLeader(request.RequestingUserId))
        {
            return Result<TeamResponse>.Failure(TeamErrors.TeamManagementRequired);
        }

        if (request.LeaderUserId != team.LeaderUserId &&
            !organization.IsOwner(request.RequestingUserId))
        {
            return Result<TeamResponse>.Failure(TeamErrors.OwnerRequired);
        }

        if (!organization.HasMember(request.LeaderUserId))
        {
            return Result<TeamResponse>.Failure(TeamErrors.UserMustBelongToOrganization);
        }

        if (await teamRepository.NameExistsAsync(
                organization.Id,
                request.Name,
                team.Id,
                cancellationToken))
        {
            return Result<TeamResponse>.Failure(TeamErrors.NameAlreadyExists);
        }

        team.Update(
            request.Name,
            request.Description,
            request.LeaderUserId,
            timeProvider.GetUtcNow());
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<TeamResponse>.Failure(TeamErrors.NameAlreadyExists);
        }

        return Result<TeamResponse>.Success(team.ToResponse());
    }
}

internal sealed class DeleteTeamCommandHandler(
    IOrganizationRepository organizationRepository,
    ITeamRepository teamRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteTeamCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        DeleteTeamCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.IsOwner(request.RequestingUserId))
        {
            return Result<Unit>.Failure(TeamErrors.OwnerRequired);
        }

        var team = await teamRepository.GetByIdAsync(
            organization.Id,
            request.TeamId,
            cancellationToken);
        if (team is null)
        {
            return Result<Unit>.Failure(TeamErrors.NotFound);
        }

        teamRepository.Remove(team);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class AddTeamMemberCommandHandler(
    IOrganizationRepository organizationRepository,
    ITeamRepository teamRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<AddTeamMemberCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        AddTeamMemberCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.HasMember(request.UserId))
        {
            return Result<Unit>.Failure(TeamErrors.UserMustBelongToOrganization);
        }

        var team = await teamRepository.GetByIdAsync(
            organization.Id,
            request.TeamId,
            cancellationToken);
        if (team is null)
        {
            return Result<Unit>.Failure(TeamErrors.NotFound);
        }

        if (!organization.IsOwner(request.RequestingUserId) &&
            !team.IsLeader(request.RequestingUserId))
        {
            return Result<Unit>.Failure(TeamErrors.TeamManagementRequired);
        }

        if (team.AddMember(request.UserId, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class RemoveTeamMemberCommandHandler(
    IOrganizationRepository organizationRepository,
    ITeamRepository teamRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveTeamMemberCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveTeamMemberCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }

        var team = await teamRepository.GetByIdAsync(
            organization.Id,
            request.TeamId,
            cancellationToken);
        if (team is null)
        {
            return Result<Unit>.Failure(TeamErrors.NotFound);
        }

        if (!organization.IsOwner(request.RequestingUserId) &&
            !team.IsLeader(request.RequestingUserId))
        {
            return Result<Unit>.Failure(TeamErrors.TeamManagementRequired);
        }

        if (team.IsLeader(request.UserId))
        {
            return Result<Unit>.Failure(TeamErrors.LeaderCannotBeRemoved);
        }

        if (team.RemoveMember(request.UserId))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class ListTeamsQueryHandler(
    IOrganizationRepository organizationRepository,
    ITeamRepository teamRepository)
    : IRequestHandler<ListTeamsQuery, Result<IReadOnlyList<TeamResponse>>>
{
    public async Task<Result<IReadOnlyList<TeamResponse>>> Handle(
        ListTeamsQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<IReadOnlyList<TeamResponse>>.Failure(OrganizationErrors.NotFound);
        }

        var teams = await teamRepository.ListAsync(organization.Id, cancellationToken);
        return Result<IReadOnlyList<TeamResponse>>.Success(
            teams.Select(team => team.ToResponse()).ToArray());
    }
}

internal sealed class GetTeamQueryHandler(
    IOrganizationRepository organizationRepository,
    ITeamRepository teamRepository)
    : IRequestHandler<GetTeamQuery, Result<TeamResponse>>
{
    public async Task<Result<TeamResponse>> Handle(
        GetTeamQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<TeamResponse>.Failure(OrganizationErrors.NotFound);
        }

        var team = await teamRepository.GetByIdAsync(
            organization.Id,
            request.TeamId,
            cancellationToken);
        return team is null
            ? Result<TeamResponse>.Failure(TeamErrors.NotFound)
            : Result<TeamResponse>.Success(team.ToResponse());
    }
}

internal sealed class ListTeamMembersQueryHandler(
    IOrganizationRepository organizationRepository,
    ITeamRepository teamRepository)
    : IRequestHandler<ListTeamMembersQuery, Result<IReadOnlyList<UserResponse>>>
{
    public async Task<Result<IReadOnlyList<UserResponse>>> Handle(
        ListTeamMembersQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<IReadOnlyList<UserResponse>>.Failure(OrganizationErrors.NotFound);
        }

        var team = await teamRepository.GetByIdAsync(
            organization.Id,
            request.TeamId,
            cancellationToken);
        if (team is null)
        {
            return Result<IReadOnlyList<UserResponse>>.Failure(TeamErrors.NotFound);
        }

        var users = await teamRepository.ListMembersAsync(team.Id, cancellationToken);
        return Result<IReadOnlyList<UserResponse>>.Success(
            users.Select(user => user.ToResponse()).ToArray());
    }
}
