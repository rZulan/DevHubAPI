using DevHub.Domain.Common;
using DevHub.Domain.Teams;

namespace DevHub.Domain.Organizations;

public sealed class Organization : BaseEntity
{
    private readonly List<OrganizationMember> _members = [];
    private readonly List<Team> _teams = [];

    private Organization()
    {
    }

    private Organization(
        Guid id,
        Guid ownerUserId,
        string name,
        string? description,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        OwnerUserId = ownerUserId;
        SetDetails(name, description);
    }

    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public IReadOnlyCollection<OrganizationMember> Members => _members;
    public IReadOnlyCollection<Team> Teams => _teams;

    public static Organization Create(
        string name,
        string? description,
        Guid creatorUserId,
        DateTimeOffset createdAtUtc)
    {
        var organization = new Organization(
            Guid.NewGuid(),
            creatorUserId,
            name,
            description,
            createdAtUtc);
        organization.AddMember(creatorUserId, createdAtUtc);
        return organization;
    }

    public void Update(string name, string? description, DateTimeOffset updatedAtUtc)
    {
        SetDetails(name, description);
        MarkUpdated(updatedAtUtc);
    }

    public bool HasMember(Guid userId) => _members.Any(member => member.UserId == userId);

    public bool IsOwner(Guid userId) => OwnerUserId == userId;

    public bool AddMember(Guid userId, DateTimeOffset joinedAtUtc)
    {
        if (HasMember(userId))
        {
            return false;
        }

        _members.Add(OrganizationMember.Create(Id, userId, joinedAtUtc));
        return true;
    }

    public bool RemoveMember(Guid userId)
    {
        if (IsOwner(userId) || _teams.Any(team => team.IsLeader(userId)))
        {
            return false;
        }

        var member = _members.SingleOrDefault(candidate => candidate.UserId == userId);
        if (member is null)
        {
            return false;
        }

        foreach (var team in _teams)
        {
            team.RemoveMember(userId);
        }

        _members.Remove(member);
        return true;
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
