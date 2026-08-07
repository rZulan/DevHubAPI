using DevHub.Domain.Common;
using DevHub.Domain.Authentication;

namespace DevHub.Domain.Users;

public sealed class User : BaseEntity
{
    private readonly List<ExternalAccount> _externalAccounts = [];

    private User()
    {
    }

    private User(
        Guid id,
        string email,
        string normalizedEmail,
        string username,
        string normalizedUsername,
        string firstName,
        string lastName,
        string? passwordHash,
        string? avatarUrl,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        Email = email;
        NormalizedEmail = normalizedEmail;
        Username = username;
        NormalizedUsername = normalizedUsername;
        FirstName = firstName;
        LastName = lastName;
        PasswordHash = passwordHash;
        AvatarUrl = avatarUrl;
    }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string Username { get; private set; } = string.Empty;

    public string NormalizedUsername { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string? PasswordHash { get; private set; }

    public string? AvatarUrl { get; private set; }

    public bool HasPassword => !string.IsNullOrWhiteSpace(PasswordHash);

    public IReadOnlyCollection<ExternalAccount> ExternalAccounts => _externalAccounts;

    public static User Create(
        string email,
        string username,
        string firstName,
        string lastName,
        string passwordHash,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User(
            Guid.NewGuid(),
            email.Trim(),
            NormalizeEmail(email),
            username.Trim(),
            NormalizeUsername(username),
            firstName.Trim(),
            lastName.Trim(),
            passwordHash,
            null,
            createdAtUtc);
    }

    public static User CreateExternal(
        string email,
        string username,
        string firstName,
        string lastName,
        string? avatarUrl,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

        return new User(
            Guid.NewGuid(),
            email.Trim(),
            NormalizeEmail(email),
            username.Trim(),
            NormalizeUsername(username),
            firstName.Trim(),
            lastName.Trim(),
            null,
            string.IsNullOrWhiteSpace(avatarUrl) ? null : avatarUrl.Trim(),
            createdAtUtc);
    }

    public void UpdateProfile(
        string username,
        string firstName,
        string lastName,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);

        Username = username.Trim();
        NormalizedUsername = NormalizeUsername(username);
        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        MarkUpdated(updatedAtUtc);
    }

    public void AddExternalAccount(ExternalAccount externalAccount)
    {
        ArgumentNullException.ThrowIfNull(externalAccount);

        if (externalAccount.UserId != Id)
        {
            throw new InvalidOperationException("The external account belongs to another user.");
        }

        _externalAccounts.Add(externalAccount);
    }

    public void RemoveExternalAccount(ExternalAccount externalAccount) =>
        _externalAccounts.Remove(externalAccount);

    public static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().ToUpperInvariant();
    }

    public static string NormalizeUsername(string username)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        return username.Trim().ToUpperInvariant();
    }
}
