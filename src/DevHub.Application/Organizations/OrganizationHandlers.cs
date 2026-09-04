using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Users;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using MediatR;
using System.Security.Cryptography;
using System.Text;

namespace DevHub.Application.Organizations;

internal static class OrganizationErrors
{
    public static readonly Error NotFound = new(
        "Organizations.NotFound",
        "The organization was not found or you are not one of its members.",
        ErrorType.NotFound);

    public static readonly Error NameAlreadyExists = new(
        "Organizations.NameAlreadyExists",
        "An organization with this name already exists.",
        ErrorType.Conflict);

    public static readonly Error UserNotFound = new(
        "Users.NotFound",
        "The requested user was not found.",
        ErrorType.NotFound);

    public static readonly Error OwnerRequired = new(
        "Organizations.OwnerRequired",
        "Only the organization owner can create invite links.",
        ErrorType.Forbidden);

    public static readonly Error PermissionRequired = new(
        "Organizations.PermissionRequired",
        "You do not have permission to perform this organization action.",
        ErrorType.Forbidden);

    public static readonly Error RoleNotFound = new(
        "Organizations.RoleNotFound",
        "The organization role was not found.",
        ErrorType.NotFound);

    public static readonly Error RoleHierarchy = new(
        "Organizations.RoleHierarchy",
        "You can only manage roles and members below your highest role.",
        ErrorType.Forbidden);

    public static readonly Error CannotGrantPermission = new(
        "Organizations.CannotGrantPermission",
        "You cannot grant a permission you do not have yourself.",
        ErrorType.Forbidden);

    public static readonly Error LastOwner = new(
        "Organizations.LastOwner",
        "Promote another organization owner before leaving.",
        ErrorType.Conflict);

    public static readonly Error ProtectedRole = new(
        "Organizations.ProtectedRole",
        "This protected role cannot be deleted or removed from its members.",
        ErrorType.Conflict);

    public static readonly Error RoleNameAlreadyExists = new(
        "Organizations.RoleNameAlreadyExists",
        "A role with this name already exists in the organization.",
        ErrorType.Conflict);

    public static readonly Error InvalidInvite = new(
        "Organizations.InvalidInvite",
        "This invite link is invalid or has expired.",
        ErrorType.NotFound);

    public static Error Invalid(IReadOnlyDictionary<string, string[]> errors) => new(
        "Organizations.ValidationFailed",
        "One or more organization fields are invalid.",
        ErrorType.Validation,
        errors);
}

internal static class OrganizationMappings
{
    public static OrganizationResponse ToResponse(this Organization organization) => new(
        organization.Id,
        organization.OwnerUserId,
        organization.Name,
        organization.Description,
        organization.CreatedAtUtc,
        organization.UpdatedAtUtc,
        organization.Members.Count,
        organization.Teams.Count);

    public static UserResponse ToResponse(this User user) => user.ToUserResponse();

    public static OrganizationRoleResponse ToResponse(
        this OrganizationRole role,
        Organization organization) => new(
        role.Id,
        role.Name,
        role.Color,
        role.Position,
        role.IsOwnerRole,
        role.IsDefaultRole,
        role.Permissions,
        organization.Members.Count(member =>
            member.RoleAssignments.Any(assignment => assignment.RoleId == role.Id)));

    public static OrganizationMemberResponse ToMemberResponse(
        this User user,
        Organization organization)
    {
        var response = user.ToUserResponse();
        var member = organization.Members.Single(candidate => candidate.UserId == user.Id);
        return new OrganizationMemberResponse(
            response.Id,
            response.Email,
            response.FirstName,
            response.LastName,
            response.CreatedAtUtc,
            response.Username,
            response.DateOfBirth,
            response.AvatarUrl,
            response.ConnectedAccounts,
            member.IsOwner,
            member.RoleAssignments.Select(assignment => assignment.RoleId).ToArray());
    }
}

internal sealed class CreateOrganizationCommandHandler(
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CreateOrganizationCommand, Result<OrganizationResponse>>
{
    public async Task<Result<OrganizationResponse>> Handle(
        CreateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.Invalid(errors));
        }

        if (await userRepository.GetByIdAsync(request.RequestingUserId, cancellationToken) is null)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.UserNotFound);
        }

        if (await organizationRepository.NameExistsAsync(request.Name, cancellationToken: cancellationToken))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NameAlreadyExists);
        }

        var organization = Organization.Create(
            request.Name,
            request.Description,
            request.RequestingUserId,
            timeProvider.GetUtcNow());
        organizationRepository.Add(organization);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NameAlreadyExists);
        }

        return Result<OrganizationResponse>.Success(organization.ToResponse());
    }
}

internal sealed class UpdateOrganizationCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateOrganizationCommand, Result<OrganizationResponse>>
{
    public async Task<Result<OrganizationResponse>> Handle(
        UpdateOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var errors = RequestValidation.Validate(request);
        if (errors.Count > 0)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.Invalid(errors));
        }

        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.HasPermission(
            request.RequestingUserId,
            OrganizationPermissions.ManageOrganization))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.PermissionRequired);
        }

        if (await organizationRepository.NameExistsAsync(
                request.Name,
                organization.Id,
                cancellationToken))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NameAlreadyExists);
        }

        organization.Update(request.Name, request.Description, timeProvider.GetUtcNow());
        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.NameAlreadyExists);
        }

        return Result<OrganizationResponse>.Success(organization.ToResponse());
    }
}

internal sealed class DeleteOrganizationCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteOrganizationCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        DeleteOrganizationCommand request,
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
            return Result<Unit>.Failure(OrganizationErrors.OwnerRequired);
        }

        organizationRepository.Remove(organization);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class AddOrganizationMemberCommandHandler(
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<AddOrganizationMemberCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        AddOrganizationMemberCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.HasPermission(
            request.RequestingUserId,
            OrganizationPermissions.ManageMembers))
        {
            return Result<Unit>.Failure(OrganizationErrors.PermissionRequired);
        }

        if (await userRepository.GetByIdAsync(request.UserId, cancellationToken) is null)
        {
            return Result<Unit>.Failure(OrganizationErrors.UserNotFound);
        }

        if (organization.AddMember(request.UserId, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class RemoveOrganizationMemberCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<RemoveOrganizationMemberCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveOrganizationMemberCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.CanManageMember(request.RequestingUserId, request.UserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.RoleHierarchy);
        }

        if (organization.RemoveMember(request.UserId, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class ListOrganizationsQueryHandler(IOrganizationRepository organizationRepository)
    : IRequestHandler<ListOrganizationsQuery, Result<IReadOnlyList<OrganizationResponse>>>
{
    public async Task<Result<IReadOnlyList<OrganizationResponse>>> Handle(
        ListOrganizationsQuery request,
        CancellationToken cancellationToken)
    {
        var organizations = await organizationRepository.ListForUserAsync(
            request.RequestingUserId,
            cancellationToken);
        return Result<IReadOnlyList<OrganizationResponse>>.Success(
            organizations.Select(organization => organization.ToResponse()).ToArray());
    }
}

internal sealed class GetOrganizationQueryHandler(IOrganizationRepository organizationRepository)
    : IRequestHandler<GetOrganizationQuery, Result<OrganizationResponse>>
{
    public async Task<Result<OrganizationResponse>> Handle(
        GetOrganizationQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        return organization is null || !organization.HasMember(request.RequestingUserId)
            ? Result<OrganizationResponse>.Failure(OrganizationErrors.NotFound)
            : Result<OrganizationResponse>.Success(organization.ToResponse());
    }
}

internal sealed class ListOrganizationMembersQueryHandler(IOrganizationRepository organizationRepository)
    : IRequestHandler<ListOrganizationMembersQuery, Result<IReadOnlyList<OrganizationMemberResponse>>>
{
    public async Task<Result<IReadOnlyList<OrganizationMemberResponse>>> Handle(
        ListOrganizationMembersQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<IReadOnlyList<OrganizationMemberResponse>>.Failure(OrganizationErrors.NotFound);
        }

        var users = await organizationRepository.ListMembersAsync(
            organization.Id,
            cancellationToken);
        return Result<IReadOnlyList<OrganizationMemberResponse>>.Success(
            users.Select(user => user.ToMemberResponse(organization)).ToArray());
    }
}

internal sealed class CreateOrganizationInviteCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CreateOrganizationInviteCommand, Result<OrganizationInviteResponse>>
{
    public async Task<Result<OrganizationInviteResponse>> Handle(
        CreateOrganizationInviteCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<OrganizationInviteResponse>.Failure(OrganizationErrors.NotFound);
        }

        if (!organization.HasPermission(
            request.RequestingUserId,
            OrganizationPermissions.CreateInvites))
        {
            return Result<OrganizationInviteResponse>.Failure(OrganizationErrors.PermissionRequired);
        }

        var now = timeProvider.GetUtcNow();
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var expiresAtUtc = now.AddDays(7);
        organizationRepository.AddInvite(OrganizationInvite.Create(
            organization.Id,
            request.RequestingUserId,
            HashToken(token),
            expiresAtUtc,
            now));
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Result<OrganizationInviteResponse>.Success(new(token, expiresAtUtc));
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}

internal sealed class AcceptOrganizationInviteCommandHandler(
    IOrganizationRepository organizationRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<AcceptOrganizationInviteCommand, Result<OrganizationResponse>>
{
    public async Task<Result<OrganizationResponse>> Handle(
        AcceptOrganizationInviteCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.InvalidInvite);
        }

        var tokenHash = Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(request.Token.Trim()))).ToLowerInvariant();
        var invite = await organizationRepository.GetInviteByTokenHashAsync(tokenHash, cancellationToken);
        if (invite is null || invite.IsExpired(timeProvider.GetUtcNow()))
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.InvalidInvite);
        }

        if (await userRepository.GetByIdAsync(request.RequestingUserId, cancellationToken) is null)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.UserNotFound);
        }

        var organization = await organizationRepository.GetByIdAsync(
            invite.OrganizationId,
            cancellationToken);
        if (organization is null)
        {
            return Result<OrganizationResponse>.Failure(OrganizationErrors.InvalidInvite);
        }

        if (organization.AddMember(request.RequestingUserId, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return Result<OrganizationResponse>.Success(organization.ToResponse());
    }
}

internal sealed class ListOrganizationRolesQueryHandler(IOrganizationRepository organizationRepository)
    : IRequestHandler<ListOrganizationRolesQuery, Result<IReadOnlyList<OrganizationRoleResponse>>>
{
    public async Task<Result<IReadOnlyList<OrganizationRoleResponse>>> Handle(
        ListOrganizationRolesQuery request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<IReadOnlyList<OrganizationRoleResponse>>.Failure(OrganizationErrors.NotFound);
        }

        return Result<IReadOnlyList<OrganizationRoleResponse>>.Success(
            organization.Roles.OrderBy(role => role.Position)
                .Select(role => role.ToResponse(organization)).ToArray());
    }
}

internal sealed class CreateOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<CreateOrganizationRoleCommand, Result<OrganizationRoleResponse>>
{
    public async Task<Result<OrganizationRoleResponse>> Handle(
        CreateOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.NotFound);
        }
        if (!organization.HasPermission(request.RequestingUserId, OrganizationPermissions.ManageRoles))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.PermissionRequired);
        }
        if (!organization.CanGrantPermissions(request.RequestingUserId, request.Permissions))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.CannotGrantPermission);
        }
        if (organization.Roles.Any(role => role.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleNameAlreadyExists);
        }

        var role = organization.AddRole(
            request.Name, request.Color, request.Permissions, timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrganizationRoleResponse>.Success(role.ToResponse(organization));
    }
}

internal sealed class UpdateOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateOrganizationRoleCommand, Result<OrganizationRoleResponse>>
{
    public async Task<Result<OrganizationRoleResponse>> Handle(
        UpdateOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.NotFound);
        }
        var role = organization.Roles.SingleOrDefault(candidate => candidate.Id == request.RoleId);
        if (role is null) return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleNotFound);
        if (role.IsOwnerRole && !organization.IsOwner(request.RequestingUserId))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.OwnerRequired);
        }
        if (!role.IsOwnerRole && !organization.CanManageRole(request.RequestingUserId, role))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleHierarchy);
        }
        if (!role.IsOwnerRole && !role.IsDefaultRole &&
            !organization.IsOwner(request.RequestingUserId) &&
            request.Position <= organization.HighestRolePosition(request.RequestingUserId))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleHierarchy);
        }
        if (!role.IsOwnerRole && !organization.CanGrantPermissions(request.RequestingUserId, request.Permissions))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.CannotGrantPermission);
        }
        if (organization.Roles.Any(candidate => candidate.Id != role.Id &&
            candidate.Name.Equals(request.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return Result<OrganizationRoleResponse>.Failure(OrganizationErrors.RoleNameAlreadyExists);
        }

        role.Update(
            request.Name,
            role.IsOwnerRole ? role.Color : request.Color,
            role.IsOwnerRole || role.IsDefaultRole ? role.Position : request.Position,
            role.IsOwnerRole ? OrganizationPermissions.All : request.Permissions,
            timeProvider.GetUtcNow());
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<OrganizationRoleResponse>.Success(role.ToResponse(organization));
    }
}

internal sealed class DeleteOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DeleteOrganizationRoleCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        DeleteOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        var role = organization.Roles.SingleOrDefault(candidate => candidate.Id == request.RoleId);
        if (role is null) return Result<Unit>.Failure(OrganizationErrors.RoleNotFound);
        if (role.IsOwnerRole || role.IsDefaultRole)
        {
            return Result<Unit>.Failure(OrganizationErrors.ProtectedRole);
        }
        if (!organization.CanManageRole(request.RequestingUserId, role))
        {
            return Result<Unit>.Failure(OrganizationErrors.RoleHierarchy);
        }

        organization.DeleteRole(role.Id);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class AssignOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<AssignOrganizationRoleCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        AssignOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        var role = organization.Roles.SingleOrDefault(candidate => candidate.Id == request.RoleId);
        if (role is null) return Result<Unit>.Failure(OrganizationErrors.RoleNotFound);
        if (role.IsOwnerRole) return Result<Unit>.Failure(OrganizationErrors.ProtectedRole);
        if (!organization.CanAssignRole(request.RequestingUserId, request.UserId, role))
        {
            return Result<Unit>.Failure(OrganizationErrors.RoleHierarchy);
        }

        if (organization.AssignRole(request.UserId, role.Id, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class RemoveOrganizationRoleCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<RemoveOrganizationRoleCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        RemoveOrganizationRoleCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        var role = organization.Roles.SingleOrDefault(candidate => candidate.Id == request.RoleId);
        if (role is null) return Result<Unit>.Failure(OrganizationErrors.RoleNotFound);
        if (role.IsOwnerRole || role.IsDefaultRole)
        {
            return Result<Unit>.Failure(OrganizationErrors.ProtectedRole);
        }
        if (!organization.CanAssignRole(request.RequestingUserId, request.UserId, role))
        {
            return Result<Unit>.Failure(OrganizationErrors.RoleHierarchy);
        }

        if (organization.RemoveRole(request.UserId, role.Id))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class PromoteOrganizationOwnerCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<PromoteOrganizationOwnerCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        PromoteOrganizationOwnerCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        if (!organization.IsOwner(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.OwnerRequired);
        }
        if (organization.PromoteOwner(request.UserId, timeProvider.GetUtcNow()))
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        return Result<Unit>.Success(Unit.Value);
    }
}

internal sealed class LeaveOrganizationCommandHandler(
    IOrganizationRepository organizationRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<LeaveOrganizationCommand, Result<Unit>>
{
    public async Task<Result<Unit>> Handle(
        LeaveOrganizationCommand request,
        CancellationToken cancellationToken)
    {
        var organization = await organizationRepository.GetByIdAsync(
            request.OrganizationId, cancellationToken);
        if (organization is null || !organization.HasMember(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.NotFound);
        }
        if (organization.IsOwner(request.RequestingUserId) &&
            !organization.CanOwnerLeave(request.RequestingUserId))
        {
            return Result<Unit>.Failure(OrganizationErrors.LastOwner);
        }
        if (!organization.RemoveMember(request.RequestingUserId, timeProvider.GetUtcNow()))
        {
            return Result<Unit>.Failure(OrganizationErrors.PermissionRequired);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }
}
