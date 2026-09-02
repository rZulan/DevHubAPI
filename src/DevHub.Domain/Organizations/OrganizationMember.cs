using DevHub.Domain.Users;

namespace DevHub.Domain.Organizations;

public sealed class OrganizationMember
{
    private readonly List<OrganizationMemberRole> _roleAssignments = [];

    private OrganizationMember()
    {
    }

    private OrganizationMember(
        Guid organizationId,
        Guid userId,
        bool isOwner,
        DateTimeOffset joinedAtUtc)
    {
        OrganizationId = organizationId;
        UserId = userId;
        IsOwner = isOwner;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }
    public bool IsOwner { get; private set; }
    public IReadOnlyCollection<OrganizationMemberRole> RoleAssignments => _roleAssignments;
    public Organization Organization { get; private set; } = null!;
    public User User { get; private set; } = null!;

    internal static OrganizationMember Create(
        Guid organizationId,
        Guid userId,
        DateTimeOffset joinedAtUtc,
        bool isOwner = false) =>
        new(organizationId, userId, isOwner, joinedAtUtc);

    internal void SetOwner(bool isOwner) => IsOwner = isOwner;

    internal bool AssignRole(Guid roleId, DateTimeOffset assignedAtUtc)
    {
        if (_roleAssignments.Any(assignment => assignment.RoleId == roleId)) return false;
        _roleAssignments.Add(OrganizationMemberRole.Create(
            OrganizationId, UserId, roleId, assignedAtUtc));
        return true;
    }

    internal bool RemoveRole(Guid roleId)
    {
        var assignment = _roleAssignments.SingleOrDefault(candidate => candidate.RoleId == roleId);
        if (assignment is null) return false;
        _roleAssignments.Remove(assignment);
        return true;
    }
}
