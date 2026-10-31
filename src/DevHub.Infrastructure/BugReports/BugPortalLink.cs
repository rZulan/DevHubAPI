namespace DevHub.Infrastructure.BugReports;

public static class BugPortalLink
{
    // An 80-bit prefix of the random portal token, encoded as 14 URL-safe characters.
    // Replacing the portal token therefore retires both link formats together.
    public static string? ShortCode(string token) => token.Length == 64 && token.All(Uri.IsHexDigit)
        ? Encode(Convert.FromHexString(token[..20])) : null;

    public static string? LookupToken(string token)
    {
        if (token.Length == 64 && token.All(Uri.IsHexDigit)) return token.ToLowerInvariant();
        if (token.Length != 14 || token.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_')) return null;
        try
        {
            var bytes = Convert.FromBase64String(token.Replace('-', '+').Replace('_', '/') + "==");
            return bytes.Length == 10 && Encode(bytes) == token ? Convert.ToHexString(bytes).ToLowerInvariant() : null;
        }
        catch (FormatException) {return null;}
    }

    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
