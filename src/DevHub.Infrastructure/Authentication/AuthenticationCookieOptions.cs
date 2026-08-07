using Microsoft.AspNetCore.Http;

namespace DevHub.Infrastructure.Authentication;

public sealed class AuthenticationCookieOptions
{
    public const string SectionName = "Authentication:Cookies";

    public bool Secure { get; init; } = true;

    public string SameSite { get; init; } = "Lax";

    public string AuthCookieName => Secure ? "__Host-DevHub.Auth" : "DevHub.Auth";

    public string RefreshCookieName => Secure ? "__Secure-DevHub.Refresh" : "DevHub.Refresh";

    public SameSiteMode GetSameSiteMode() =>
        Enum.TryParse<SameSiteMode>(SameSite, true, out var value)
            ? value
            : SameSiteMode.Lax;
}
