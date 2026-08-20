using DevHub.Domain.Common;
using DevHub.Domain.Organizations;

namespace DevHub.Domain.Teams;

public sealed class Team : BaseEntity
{
    private readonly List<TeamMember> _members = [];

    private Team()
    {
    }

    private Team(
        Guid id,
        Guid organizationId,
        Guid leaderUserId,
        string name,
        string? description,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        OrganizationId = organizationId;
        LeaderUserId = leaderUserId;
        SetDetails(name, description);
        AddMember(leaderUserId, createdAtUtc);
    }

    public Guid OrganizationId { get; private set; }
    public Guid LeaderUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Organization Organization { get; private set; } = null!;
    public IReadOnlyCollection<TeamMember> Members => _members;

    public static Team Create(
        Guid organizationId,
        Guid leaderUserId,
        string name,
        string? description,
        DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), organizationId, leaderUserId, name, description, createdAtUtc);

    public void Update(
        string name,
        string? description,
        Guid leaderUserId,
        DateTimeOffset updatedAtUtc)
    {
        SetDetails(name, description);
        LeaderUserId = leaderUserId;
        AddMember(leaderUserId, updatedAtUtc);
        MarkUpdated(updatedAtUtc);
    }

    public bool HasMember(Guid userId) => _members.Any(member => member.UserId == userId);

    public bool IsLeader(Guid userId) => LeaderUserId == userId;

    public bool AddMember(Guid userId, DateTimeOffset joinedAtUtc)
    {
        if (HasMember(userId))
        {
            return false;
        }

        _members.Add(TeamMember.Create(Id, userId, joinedAtUtc));
        return true;
    }

    public bool RemoveMember(Guid userId)
    {
        if (IsLeader(userId))
        {
            return false;
        }

        var member = _members.SingleOrDefault(candidate => candidate.UserId == userId);
        return member is not null && _members.Remove(member);
    }

    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim().ToUpperInvariant();
    }

    private void SetDetails(string name, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        NormalizedName = NormalizeName(name);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
    }
}
