using System.Text.RegularExpressions;
using DevHub.Application.Abstractions.Persistence;

namespace DevHub.Application.Users;

internal static partial class UsernamePolicy
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin",
        "api",
        "settings",
        "support"
    };

    public static string? GetValidationError(string username)
    {
        if (string.IsNullOrWhiteSpace(username) || !ValidUsernameRegex().IsMatch(username))
        {
            return "Username must be 3-30 characters and contain only lowercase letters, numbers, underscores, or hyphens.";
        }

        return Reserved.Contains(username)
            ? "This username is reserved."
            : null;
    }

    public static async Task<string> CreateAvailableAsync(
        string preferredValue,
        IUserRepository userRepository,
        CancellationToken cancellationToken)
    {
        var candidate = Sanitize(preferredValue);

        if (GetValidationError(candidate) is null &&
            !await userRepository.UsernameExistsAsync(candidate, cancellationToken: cancellationToken))
        {
            return candidate;
        }

        var prefix = candidate.Length > 23 ? candidate[..23].TrimEnd('-', '_') : candidate;

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var suffix = Guid.NewGuid().ToString("N")[..6];
            var uniqueCandidate = $"{prefix}-{suffix}";

            if (!await userRepository.UsernameExistsAsync(
                    uniqueCandidate,
                    cancellationToken: cancellationToken))
            {
                return uniqueCandidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique username.");
    }

    private static string Sanitize(string value)
    {
        var sanitized = InvalidUsernameCharactersRegex()
            .Replace(value.Trim().ToLowerInvariant(), "-")
            .Trim('-', '_');

        if (sanitized.Length > 30)
        {
            sanitized = sanitized[..30].TrimEnd('-', '_');
        }

        return sanitized.Length >= 3 ? sanitized : $"user-{sanitized}".TrimEnd('-');
    }

    [GeneratedRegex("^[a-z0-9_-]{3,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidUsernameRegex();

    [GeneratedRegex("[^a-z0-9_-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidUsernameCharactersRegex();
}
