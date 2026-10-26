using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Domain.Organizations;
using MediatR;

namespace DevHub.Application.Organizations;

public sealed record ActiveOrganizationInviteResponse(Guid Id, Guid CreatedByUserId, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);
public sealed record ListOrganizationInvitesQuery(Guid OrganizationId, Guid RequestingUserId) : IRequest<Result<IReadOnlyList<ActiveOrganizationInviteResponse>>>;
public sealed record RevokeOrganizationInviteCommand(Guid OrganizationId, Guid InviteId, Guid RequestingUserId) : IRequest<Result<Unit>>;

internal sealed class ListOrganizationInvitesQueryHandler(IOrganizationRepository organizations, TimeProvider timeProvider)
    : IRequestHandler<ListOrganizationInvitesQuery, Result<IReadOnlyList<ActiveOrganizationInviteResponse>>>
{
    public async Task<Result<IReadOnlyList<ActiveOrganizationInviteResponse>>> Handle(ListOrganizationInvitesQuery request, CancellationToken ct)
    {
        var organization = await organizations.GetByIdAsync(request.OrganizationId, ct);
        if (organization is null || !organization.HasMember(request.RequestingUserId)) return Result<IReadOnlyList<ActiveOrganizationInviteResponse>>.Failure(OrganizationErrors.NotFound);
        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.CreateInvites)) return Result<IReadOnlyList<ActiveOrganizationInviteResponse>>.Failure(OrganizationErrors.PermissionRequired);
        var canManage = organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageInvites);
        var invites = await organizations.ListActiveInvitesAsync(organization.Id, canManage ? null : request.RequestingUserId, timeProvider.GetUtcNow(), ct);
        return Result<IReadOnlyList<ActiveOrganizationInviteResponse>>.Success(invites.Select(invite => new ActiveOrganizationInviteResponse(invite.Id, invite.CreatedByUserId, invite.CreatedAtUtc, invite.ExpiresAtUtc)).ToArray());
    }
}

internal sealed class RevokeOrganizationInviteCommandHandler(IOrganizationRepository organizations, IUnitOfWork unitOfWork, TimeProvider timeProvider)
    : IRequestHandler<RevokeOrganizationInviteCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(RevokeOrganizationInviteCommand request, CancellationToken ct)
    {
        var organization = await organizations.GetByIdAsync(request.OrganizationId, ct);
        if (organization is null || !organization.HasMember(request.RequestingUserId)) return Result<Unit>.Failure(OrganizationErrors.NotFound);
        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.CreateInvites)) return Result<Unit>.Failure(OrganizationErrors.PermissionRequired);
        var invite = await organizations.GetInviteByIdAsync(organization.Id, request.InviteId, ct);
        if (invite is null) return Result<Unit>.Failure(OrganizationErrors.InvalidInvite);
        if (invite.CreatedByUserId != request.RequestingUserId && !organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageInvites)) return Result<Unit>.Failure(OrganizationErrors.PermissionRequired);
        invite.Revoke(timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
        return Result<Unit>.Success(Unit.Value);
    }
}
