using System.ComponentModel.DataAnnotations;
using DevHub.Application.Common;
using DevHub.Application.Users;
using MediatR;

namespace DevHub.Application.Teams;

public sealed record TeamResponse(
    Guid Id,
    Guid OrganizationId,
    Guid LeaderUserId,
    string Name,
    string? Description,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc,
    int MemberCount,
    IReadOnlyList<Guid> MemberIds);

public sealed record CreateTeamCommand(
    Guid OrganizationId,
    Guid RequestingUserId,
    Guid LeaderUserId,
    [property: Required, StringLength(150)] string Name,
    [property: StringLength(1000)] string? Description)
    : IRequest<Result<TeamResponse>>;

public sealed record UpdateTeamCommand(
    Guid OrganizationId,
    Guid TeamId,
    Guid RequestingUserId,
    Guid LeaderUserId,
    [property: Required, StringLength(150)] string Name,
    [property: StringLength(1000)] string? Description)
    : IRequest<Result<TeamResponse>>;

public sealed record DeleteTeamCommand(Guid OrganizationId, Guid TeamId, Guid RequestingUserId)
    : IRequest<Result<Unit>>;

public sealed record AddTeamMemberCommand(
    Guid OrganizationId,
    Guid TeamId,
    Guid UserId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record RemoveTeamMemberCommand(
    Guid OrganizationId,
    Guid TeamId,
    Guid UserId,
    Guid RequestingUserId) : IRequest<Result<Unit>>;

public sealed record ListTeamsQuery(Guid OrganizationId, Guid RequestingUserId)
    : IRequest<Result<IReadOnlyList<TeamResponse>>>;

public sealed record GetTeamQuery(Guid OrganizationId, Guid TeamId, Guid RequestingUserId)
    : IRequest<Result<TeamResponse>>;

public sealed record ListTeamMembersQuery(Guid OrganizationId, Guid TeamId, Guid RequestingUserId)
    : IRequest<Result<IReadOnlyList<UserResponse>>>;
