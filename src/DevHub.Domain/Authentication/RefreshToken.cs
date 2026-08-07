using DevHub.Domain.Common;

namespace DevHub.Domain.Authentication;

public sealed class RefreshToken : BaseEntity
{
    private RefreshToken()
    {
    }

    private RefreshToken(
        Guid id,
        Guid userId,
        Guid familyId,
        string tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc)
        : base(id, createdAtUtc)
    {
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset ExpiresAtUtc { get; private set; }

    public DateTimeOffset? RevokedAtUtc { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public bool IsActive(DateTimeOffset now) =>
        RevokedAtUtc is null && ExpiresAtUtc > now;

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAtUtc,
        DateTimeOffset expiresAtUtc,
        Guid? familyId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (expiresAtUtc <= createdAtUtc)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expiresAtUtc),
                "The refresh token expiration must be after its creation time.");
        }

        return new RefreshToken(
            Guid.NewGuid(),
            userId,
            familyId ?? Guid.NewGuid(),
            tokenHash,
            createdAtUtc,
            expiresAtUtc);
    }

    public void Rotate(Guid replacementTokenId, DateTimeOffset revokedAtUtc)
    {
        if (RevokedAtUtc is not null)
        {
            throw new InvalidOperationException("The refresh token has already been revoked.");
        }

        RevokedAtUtc = revokedAtUtc;
        ReplacedByTokenId = replacementTokenId;
        MarkUpdated(revokedAtUtc);
    }
}
