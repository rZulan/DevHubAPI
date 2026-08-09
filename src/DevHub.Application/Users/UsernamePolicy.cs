using System.Text.RegularExpressions;
using DevHub.Application.Abstractions.Persistence;

namespace DevHub.Application.Users;

internal static partial class UsernamePolicy
{
    private const int MinimumLength = 5;
    private const int MaximumLength = 15;
    private const int UniqueSuffixLength = 6;

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
            return "Username must be 5-15 characters and contain only letters, numbers, or underscores.";
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

        var maximumPrefixLength = MaximumLength - UniqueSuffixLength - 1;
        var prefix = candidate[..Math.Min(candidate.Length, maximumPrefixLength)].TrimEnd('_');

        if (prefix.Length == 0)
        {
            prefix = "user";
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var suffix = Guid.NewGuid().ToString("N")[..UniqueSuffixLength];
            var uniqueCandidate = $"{prefix}_{suffix}";

            if (GetValidationError(uniqueCandidate) is null &&
                !await userRepository.UsernameExistsAsync(
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
            .Replace(value.Trim().ToLowerInvariant(), "_")
            .Trim('_');

        if (sanitized.Length > MaximumLength)
        {
            sanitized = sanitized[..MaximumLength].TrimEnd('_');
        }

        if (sanitized.Length >= MinimumLength)
        {
            return sanitized;
        }

        return $"user_{sanitized}"[..Math.Min(MaximumLength, sanitized.Length + 5)];
    }

    [GeneratedRegex("^[A-Za-z0-9_]{5,15}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidUsernameRegex();

    [GeneratedRegex("[^A-Za-z0-9_]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidUsernameCharactersRegex();
}
