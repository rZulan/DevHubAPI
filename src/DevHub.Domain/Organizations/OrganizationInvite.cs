using DevHub.Domain.Common;

namespace DevHub.Domain.Organizations;

public sealed class OrganizationInvite : BaseEntity
{
    private OrganizationInvite()
    {
    }

    private OrganizationInvite(
        Guid id,
        Guid organizationId,
        Guid createdByUserId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        OrganizationId = organizationId;
        CreatedByUserId = createdByUserId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid OrganizationId { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public Organization Organization { get; private set; } = null!;

    public static OrganizationInvite Create(
        Guid organizationId,
        Guid createdByUserId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), organizationId, createdByUserId, tokenHash, expiresAtUtc, createdAtUtc);

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAtUtc;
}
