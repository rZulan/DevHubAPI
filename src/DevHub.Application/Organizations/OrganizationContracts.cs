using System.ComponentModel.DataAnnotations;
using DevHub.Application.Common;
using DevHub.Application.Users;
using MediatR;

namespace DevHub.Application.Organizations;

public sealed record OrganizationResponse(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int MemberCount,
    int TeamCount);

public sealed record CreateOrganizationCommand(
    Guid RequestingUserId,
    [property: Required, StringLength(150)] string Name,
    [property: StringLength(1000)] string? Description)
    : IRequest<Result<OrganizationResponse>>;

public sealed record UpdateOrganizationCommand(
    Guid OrganizationId,
    Guid RequestingUserId,
    [property: Required, StringLength(150)] string Name,
    [property: StringLength(1000)] string? Description)
    : IRequest<Result<OrganizationResponse>>;

public sealed record DeleteOrganizationCommand(Guid OrganizationId, Guid RequestingUserId)
    : IRequest<Result<Unit>>;

public sealed record AddOrganizationMemberCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record RemoveOrganizationMemberCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record ListOrganizationsQuery(Guid RequestingUserId)
    : IRequest<Result<IReadOnlyList<OrganizationResponse>>>;

public sealed record GetOrganizationQuery(Guid OrganizationId, Guid RequestingUserId)
    : IRequest<Result<OrganizationResponse>>;

public sealed record ListOrganizationMembersQuery(Guid OrganizationId, Guid RequestingUserId)
    : IRequest<Result<IReadOnlyList<UserResponse>>>;
