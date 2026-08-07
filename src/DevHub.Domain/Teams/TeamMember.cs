using DevHub.Domain.Users;

namespace DevHub.Domain.Teams;

public sealed class TeamMember
{
    private TeamMember()
    {
    }

    private TeamMember(Guid teamId, Guid userId, DateTimeOffset joinedAtUtc)
    {
        TeamId = teamId;
        UserId = userId;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid TeamId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }
    public Team Team { get; private set; } = null!;
    public User User { get; private set; } = null!;

    internal static TeamMember Create(Guid teamId, Guid userId, DateTimeOffset joinedAtUtc) =>
        new(teamId, userId, joinedAtUtc);
}
