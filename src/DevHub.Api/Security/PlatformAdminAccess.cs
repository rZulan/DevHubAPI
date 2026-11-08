using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace DevHub.Api.Security;

public sealed class PlatformAdminAccess(IConfiguration configuration)
{
    public const string Policy = "PlatformAdmin";
    private readonly Uri? _origin = BrowserOriginPolicy.Normalize(configuration["PlatformAdmin:Origin"]) is { } origin
        ? new Uri(origin)
        : null;
    private readonly HashSet<Guid> _userIds = (configuration.GetSection("PlatformAdmin:UserIds").Get<Guid[]>() ?? [])
        .Where(id => id != Guid.Empty).ToHashSet();

    public string? Origin => _origin?.GetLeftPart(UriPartial.Authority);

    public bool IsAdminHost(HttpRequest request) => _origin is not null &&
        (request.IsHttps || _origin.Scheme == Uri.UriSchemeHttp) &&
        string.Equals(_origin.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);

    public void ConfigurePolicy(AuthorizationPolicyBuilder policy) => policy
        .AddAuthenticationSchemes(CookieAuthenticationDefaults.AuthenticationScheme)
        .RequireAuthenticatedUser()
        .RequireAssertion(context => context.Resource is HttpContext http && IsAdminHost(http.Request) &&
            Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId) &&
            _userIds.Contains(userId));
}
