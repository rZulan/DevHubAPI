using DevHub.Domain.Common;
using DevHub.Domain.Teams;

namespace DevHub.Domain.Organizations;

public sealed class Organization : BaseEntity
{
    private readonly List<OrganizationMember> _members = [];
    private readonly List<OrganizationRole> _roles = [];
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
    public IReadOnlyCollection<OrganizationRole> Roles => _roles;
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
        var ownerRole = OrganizationRole.CreateOwner(organization.Id, createdAtUtc);
        var memberRole = OrganizationRole.CreateMember(organization.Id, createdAtUtc);
        organization._roles.Add(ownerRole);
        organization._roles.Add(memberRole);
        var creator = OrganizationMember.Create(
            organization.Id, creatorUserId, createdAtUtc, isOwner: true);
        creator.AssignRole(ownerRole.Id, createdAtUtc);
        creator.AssignRole(memberRole.Id, createdAtUtc);
        organization._members.Add(creator);
        return organization;
    }

    public void Update(string name, string? description, DateTimeOffset updatedAtUtc)
    {
        SetDetails(name, description);
        MarkUpdated(updatedAtUtc);
    }

    public bool HasMember(Guid userId) => _members.Any(member => member.UserId == userId);

    public bool IsOwner(Guid userId) =>
        _members.Any(member => member.UserId == userId && member.IsOwner);

    public bool HasPermission(Guid userId, string permission)
    {
        if (IsOwner(userId)) return true;
        var member = _members.SingleOrDefault(candidate => candidate.UserId == userId);
        return member is not null && member.RoleAssignments.Any(assignment =>
            _roles.SingleOrDefault(role => role.Id == assignment.RoleId)?.HasPermission(permission) == true);
    }

    public int HighestRolePosition(Guid userId)
    {
        if (IsOwner(userId)) return 0;
        var member = _members.SingleOrDefault(candidate => candidate.UserId == userId);
        return member?.RoleAssignments
            .Select(assignment => _roles.SingleOrDefault(role => role.Id == assignment.RoleId)?.Position ?? int.MaxValue)
            .DefaultIfEmpty(int.MaxValue)
            .Min() ?? int.MaxValue;
    }

    public bool CanManageMember(Guid requestingUserId, Guid targetUserId) =>
        requestingUserId != targetUserId &&
        HasPermission(requestingUserId, OrganizationPermissions.ManageMembers) &&
        !IsOwner(targetUserId) &&
        HighestRolePosition(requestingUserId) < HighestRolePosition(targetUserId);

    public bool CanManageRole(Guid requestingUserId, OrganizationRole role) =>
        IsOwner(requestingUserId)
            ? true
            : HasPermission(requestingUserId, OrganizationPermissions.ManageRoles) &&
              !role.IsOwnerRole &&
              HighestRolePosition(requestingUserId) < role.Position;

    public bool CanGrantPermissions(Guid requestingUserId, IEnumerable<string> permissions) =>
        IsOwner(requestingUserId) ||
        permissions.All(permission => HasPermission(requestingUserId, permission));

    public bool AddMember(Guid userId, DateTimeOffset joinedAtUtc)
    {
        if (HasMember(userId))
        {
            return false;
        }

        var member = OrganizationMember.Create(Id, userId, joinedAtUtc);
        var defaultRole = _roles
            .Where(role => !role.IsOwnerRole)
            .OrderByDescending(role => role.Position)
            .FirstOrDefault();
        if (defaultRole is not null) member.AssignRole(defaultRole.Id, joinedAtUtc);
        _members.Add(member);
        return true;
    }

    public OrganizationRole AddRole(
        string name,
        string color,
        IEnumerable<string> permissions,
        DateTimeOffset createdAtUtc)
    {
        var nextPosition = _roles.Where(role => !role.IsOwnerRole && !role.IsDefaultRole)
            .Select(role => role.Position)
            .DefaultIfEmpty(0)
            .Max() + 1;
        var role = OrganizationRole.Create(
            Id, name, color, nextPosition, permissions, createdAtUtc);
        _roles.Add(role);
        return role;
    }

    public bool DeleteRole(Guid roleId)
    {
        var role = _roles.SingleOrDefault(candidate => candidate.Id == roleId);
        if (role is null || role.IsOwnerRole || role.IsDefaultRole) return false;
        foreach (var member in _members) member.RemoveRole(roleId);
        _roles.Remove(role);
        return true;
    }

    public bool AssignRole(Guid userId, Guid roleId, DateTimeOffset assignedAtUtc)
    {
        var member = _members.SingleOrDefault(candidate => candidate.UserId == userId);
        var role = _roles.SingleOrDefault(candidate => candidate.Id == roleId);
        return member is not null && role is not null && member.AssignRole(roleId, assignedAtUtc);
    }

    public bool RemoveRole(Guid userId, Guid roleId)
    {
        var member = _members.SingleOrDefault(candidate => candidate.UserId == userId);
        var role = _roles.SingleOrDefault(candidate => candidate.Id == roleId);
        if (member is null || role is null || role.IsOwnerRole) return false;
        return member.RemoveRole(roleId);
    }

    public bool PromoteOwner(Guid userId, DateTimeOffset assignedAtUtc)
    {
        var member = _members.SingleOrDefault(candidate => candidate.UserId == userId);
        var ownerRole = _roles.Single(role => role.IsOwnerRole);
        if (member is null || member.IsOwner) return false;
        member.SetOwner(true);
        member.AssignRole(ownerRole.Id, assignedAtUtc);
        return true;
    }

    public bool CanOwnerLeave(Guid userId) =>
        IsOwner(userId) && _members.Count(member => member.IsOwner) > 1;

    public bool CanAssignRole(Guid requestingUserId, Guid targetUserId, OrganizationRole role) =>
        requestingUserId != targetUserId &&
        CanManageRole(requestingUserId, role) &&
        !IsOwner(targetUserId) &&
        HighestRolePosition(requestingUserId) < HighestRolePosition(targetUserId);

    public bool RemoveMember(Guid userId, DateTimeOffset removedAtUtc)
    {
        var leavingOwner = IsOwner(userId);
        if (leavingOwner && !CanOwnerLeave(userId))
        {
            return false;
        }

        var member = _members.SingleOrDefault(candidate => candidate.UserId == userId);
        if (member is null)
        {
            return false;
        }

        var replacementOwner = leavingOwner
            ? _members.First(member => member.IsOwner && member.UserId != userId)
            : null;
        if (!leavingOwner && _teams.Any(team => team.IsLeader(userId))) return false;

        foreach (var team in _teams)
        {
            if (team.IsLeader(userId) && replacementOwner is not null)
            {
                team.Update(team.Name, team.Description, replacementOwner.UserId, removedAtUtc);
            }
            team.RemoveMember(userId);
        }

        _members.Remove(member);
        if (OwnerUserId == userId)
        {
            OwnerUserId = _members.First(candidate => candidate.IsOwner).UserId;
        }
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
