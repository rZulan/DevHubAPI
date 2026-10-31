using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Domain.Organizations;
using MediatR;

namespace DevHub.Application.Organizations;

internal sealed class SetOrganizationAvatarCommandHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork, TimeProvider clock)
    : IRequestHandler<SetOrganizationAvatarCommand, Result<OrganizationResponse>>
{
    public async Task<Result<OrganizationResponse>> Handle(SetOrganizationAvatarCommand request, CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Validate(request);
        if (errors.Count > 0) return Result<OrganizationResponse>.Failure(OrganizationErrors.Invalid(errors));
        var organization = await organizations.GetByIdAsync(request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId)) return Result<OrganizationResponse>.Failure(OrganizationErrors.NotFound);
        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageOrganization)) return Result<OrganizationResponse>.Failure(OrganizationErrors.PermissionRequired);
        if (organization.AvatarUrl != request.AvatarUrl)
        {
            organization.SetAvatar(request.AvatarUrl, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result<OrganizationResponse>.Success(organization.ToResponse());
    }
}
