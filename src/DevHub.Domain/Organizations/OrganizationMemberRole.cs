namespace DevHub.Domain.Organizations;

public sealed class OrganizationMemberRole
{
    private OrganizationMemberRole()
    {
    }

    private OrganizationMemberRole(
        Guid organizationId,
        Guid userId,
        Guid roleId,
        DateTimeOffset assignedAtUtc)
    {
        OrganizationId = organizationId;
        UserId = userId;
        RoleId = roleId;
        AssignedAtUtc = assignedAtUtc;
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
    public OrganizationMember Member { get; private set; } = null!;
    public OrganizationRole Role { get; private set; } = null!;

    internal static OrganizationMemberRole Create(
        Guid organizationId,
        Guid userId,
        Guid roleId,
        DateTimeOffset assignedAtUtc) =>
        new(organizationId, userId, roleId, assignedAtUtc);
}
