using System.Security.Cryptography;
using System.Text;

namespace DevHub.Application.Organizations;

internal static class OrganizationInviteCode
{
    // 60 random bits, with no ambiguous I/1 or O/0 characters.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public static string Create() => RandomNumberGenerator.GetString(Alphabet, 12);

    public static string? Normalize(string? value)
    {
        var code = value?.Trim();
        if (code is null) return null;
        if (code.Length == 12 && code.All(character => Alphabet.Contains(char.ToUpperInvariant(character)))) return code.ToUpperInvariant();
        // Keep existing invitations valid until their normal expiry or revocation.
        if (code.Length == 64 && code.All(Uri.IsHexDigit)) return code.ToLowerInvariant();
        return null;
    }

    public static string Hash(string code) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
