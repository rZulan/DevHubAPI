using System.ComponentModel.DataAnnotations;
using DevHub.Application.Common;
using DevHub.Application.Users;
using MediatR;

namespace DevHub.Application.Organizations;

public sealed record OrganizationResponse(
    Guid Id,
    Guid OwnerUserId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int MemberCount,
    int TeamCount);

public sealed record OrganizationRoleResponse(
    Guid Id,
    string Name,
    string Color,
    int Position,
    bool IsOwnerRole,
    bool IsDefaultRole,
    IReadOnlyList<string> Permissions,
    int MemberCount);

public sealed record OrganizationMemberResponse(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    DateTimeOffset CreatedAtUtc,
    string? Username,
    DateOnly? DateOfBirth,
    string? AvatarUrl,
    IReadOnlyCollection<ConnectedAccountResponse> ConnectedAccounts,
    bool IsOwner,
    IReadOnlyList<Guid> RoleIds,
    DateTimeOffset JoinedAtUtc);

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
    : IRequest<Result<IReadOnlyList<OrganizationMemberResponse>>>;

public sealed record ListOrganizationRolesQuery(Guid OrganizationId, Guid RequestingUserId)
    : IRequest<Result<IReadOnlyList<OrganizationRoleResponse>>>;

public sealed record CreateOrganizationRoleCommand(
    Guid OrganizationId,
    Guid RequestingUserId,
    [property: Required, StringLength(100)] string Name,
    [property: Required, StringLength(20)] string Color,
    IReadOnlyList<string> Permissions)
    : IRequest<Result<OrganizationRoleResponse>>;

public sealed record UpdateOrganizationRoleCommand(
    Guid OrganizationId,
    Guid RoleId,
    Guid RequestingUserId,
    [property: Required, StringLength(100)] string Name,
    [property: Required, StringLength(20)] string Color,
    int Position,
    IReadOnlyList<string> Permissions)
    : IRequest<Result<OrganizationRoleResponse>>;

public sealed record DeleteOrganizationRoleCommand(
    Guid OrganizationId,
    Guid RoleId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record AssignOrganizationRoleCommand(
    Guid OrganizationId,
    Guid RoleId,
    Guid UserId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record RemoveOrganizationRoleCommand(
    Guid OrganizationId,
    Guid RoleId,
    Guid UserId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record PromoteOrganizationOwnerCommand(
    Guid OrganizationId,
    Guid UserId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record LeaveOrganizationCommand(
    Guid OrganizationId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record OrganizationInviteResponse(string Token, DateTimeOffset ExpiresAtUtc);

public sealed record CreateOrganizationInviteCommand(Guid OrganizationId, Guid RequestingUserId)
    : IRequest<Result<OrganizationInviteResponse>>;

public sealed record AcceptOrganizationInviteCommand(string Token, Guid RequestingUserId)
    : IRequest<Result<OrganizationResponse>>;

public sealed record DashboardWidgetDefinition(
    string Id,
    string Type,
    string Size,
    string? Content,
    double Height = 1,
    double? Width = null,
    string? SectionId = null);

public sealed record OrganizationDashboardResponse(
    IReadOnlyList<DashboardWidgetDefinition> Widgets,
    DateTimeOffset? PublishedAtUtc);

public sealed record GetOrganizationDashboardQuery(Guid OrganizationId, Guid RequestingUserId)
    : IRequest<Result<OrganizationDashboardResponse>>;

public sealed record PublishOrganizationDashboardCommand(
    Guid OrganizationId,
    Guid RequestingUserId,
    IReadOnlyList<DashboardWidgetDefinition> Widgets)
    : IRequest<Result<OrganizationDashboardResponse>>;
