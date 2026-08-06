namespace ProjectRoadMapper.Domain.Users;

public sealed class User
{
    private User()
    {
    }

    private User(
        Guid id,
        string email,
        string normalizedEmail,
        string firstName,
        string lastName,
        string passwordHash,
        DateTimeOffset createdAtUtc)
    {
        Id = id;
        Email = email;
        NormalizedEmail = normalizedEmail;
        FirstName = firstName;
        LastName = lastName;
        PasswordHash = passwordHash;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string NormalizedEmail { get; private set; } = string.Empty;

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static User Create(
        string email,
        string firstName,
        string lastName,
        string passwordHash,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(firstName);
        ArgumentException.ThrowIfNullOrWhiteSpace(lastName);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        return new User(
            Guid.NewGuid(),
            email.Trim(),
            NormalizeEmail(email),
            firstName.Trim(),
            lastName.Trim(),
            passwordHash,
            createdAtUtc);
    }

    public static string NormalizeEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return email.Trim().ToUpperInvariant();
    }
}
