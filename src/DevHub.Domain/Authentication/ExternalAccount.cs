using DevHub.Domain.Common;

namespace DevHub.Domain.Authentication;

public sealed class ExternalAccount : BaseEntity
{
    private ExternalAccount()
    {
    }

    private ExternalAccount(
        Guid id,
        Guid userId,
        string provider,
        string providerUserId,
        string? providerUsername,
        string? providerEmail,
        string? avatarUrl,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        UserId = userId;
        Provider = provider;
        ProviderUserId = providerUserId;
        ProviderUsername = providerUsername;
        ProviderEmail = providerEmail;
        AvatarUrl = avatarUrl;
    }

    public Guid UserId { get; private set; }

    public string Provider { get; private set; } = string.Empty;

    public string ProviderUserId { get; private set; } = string.Empty;

    public string? ProviderUsername { get; private set; }

    public string? ProviderEmail { get; private set; }

    public string? AvatarUrl { get; private set; }

    public static ExternalAccount Create(
        Guid userId,
        string provider,
        string providerUserId,
        string? providerUsername,
        string? providerEmail,
        string? avatarUrl,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerUserId);

        return new ExternalAccount(
            Guid.NewGuid(),
            userId,
            provider.Trim().ToLowerInvariant(),
            providerUserId.Trim(),
            Clean(providerUsername),
            Clean(providerEmail),
            Clean(avatarUrl),
            createdAtUtc);
    }

    public void UpdateProviderProfile(
        string? providerUsername,
        string? providerEmail,
        string? avatarUrl,
        DateTimeOffset updatedAtUtc)
    {
        ProviderUsername = Clean(providerUsername);
        ProviderEmail = Clean(providerEmail);
        AvatarUrl = Clean(avatarUrl);
        MarkUpdated(updatedAtUtc);
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
