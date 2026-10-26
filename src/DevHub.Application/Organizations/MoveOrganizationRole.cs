using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using MediatR;

namespace DevHub.Application.Organizations;

public sealed record MoveOrganizationRoleCommand(Guid OrganizationId, Guid RoleId, Guid RequestingUserId, int Direction) : IRequest<Result<Unit>>;
internal sealed class MoveOrganizationRoleCommandHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : IRequestHandler<MoveOrganizationRoleCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(MoveOrganizationRoleCommand request, CancellationToken ct)
    {
        var organization = await organizations.GetByIdAsync(request.OrganizationId, ct);
        if (organization is null || !organization.HasMember(request.RequestingUserId)) return Result<Unit>.Failure(OrganizationErrors.NotFound);
        if (!organization.MoveRole(request.RequestingUserId, request.RoleId, request.Direction, timeProvider.GetUtcNow())) return Result<Unit>.Failure(OrganizationErrors.RoleHierarchy);
        // Both position changes are committed by one atomic SaveChanges operation.
        await unitOfWork.SaveChangesAsync(ct);
        return Result<Unit>.Success(Unit.Value);
    }
}
