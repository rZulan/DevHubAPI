using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Organizations;
using DevHub.Domain.Organizations;
using MediatR;

namespace DevHub.Application.Teams;

internal sealed class SetTeamAvatarCommandHandler(IOrganizationRepository organizations, ITeamRepository teams, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<SetTeamAvatarCommand, Result<TeamResponse>>
{
    public async Task<Result<TeamResponse>> Handle(SetTeamAvatarCommand request, CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Validate(request);
        if (errors.Count > 0) return Result<TeamResponse>.Failure(TeamErrors.Invalid(errors));
        var organization = await organizations.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId)) return Result<TeamResponse>.Failure(OrganizationErrors.NotFound);
        var team = await teams.GetByIdAsync(request.OrganizationId, request.TeamId, cancellationToken);
        if (team is null || team.OrganizationId != organization.Id) return Result<TeamResponse>.Failure(TeamErrors.NotFound);
        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageTeams) && !team.IsLeader(request.RequestingUserId)) return Result<TeamResponse>.Failure(TeamErrors.TeamManagementRequired);
        if (team.AvatarUrl != request.AvatarUrl)
        {
            team.SetAvatar(request.AvatarUrl, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result<TeamResponse>.Success(team.ToResponse());
    }
}
