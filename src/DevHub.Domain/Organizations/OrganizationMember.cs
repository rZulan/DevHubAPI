using DevHub.Domain.Users;

namespace DevHub.Domain.Organizations;

public sealed class OrganizationMember
{
    private OrganizationMember()
    {
    }

    private OrganizationMember(Guid organizationId, Guid userId, DateTimeOffset joinedAtUtc)
    {
        OrganizationId = organizationId;
        UserId = userId;
        JoinedAtUtc = joinedAtUtc;
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTimeOffset JoinedAtUtc { get; private set; }
    public Organization Organization { get; private set; } = null!;
    public User User { get; private set; } = null!;

    internal static OrganizationMember Create(
        Guid organizationId,
        Guid userId,
        DateTimeOffset joinedAtUtc) =>
        new(organizationId, userId, joinedAtUtc);
}
