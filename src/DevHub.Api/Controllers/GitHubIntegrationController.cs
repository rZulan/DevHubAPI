using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevHub.Infrastructure.GitHub;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.WebUtilities;

namespace DevHub.Api.Controllers;

public sealed class GitHubIntegrationErrorFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var (status, message) = context.Exception switch {
            GitHubIntegrationException e => (e.Status, e.Message),
            CryptographicException => (409, "Reconnect GitHub to restore repository access."),
            HttpRequestException => (502, "GitHub is temporarily unavailable. Please try again."),
            TaskCanceledException => (504, "GitHub did not respond in time. Please try again."),
            _ => (0, "")
        };
        if (status == 0) return;
        context.Result = new ObjectResult(new ProblemDetails {Status = status, Title = "GitHub connection", Detail = message}) {StatusCode = status};
        context.ExceptionHandled = true;
    }
}

[Authorize]
[Route("api/integrations/github")]
[TypeFilter(typeof(GitHubIntegrationErrorFilter))]
public sealed class GitHubIntegrationController(GitHubIntegration github, IDataProtectionProvider protection, IConfiguration config, TimeProvider clock) : ApiControllerBase
{
    private const string FlowCookie = "__Host-DevHub.GitHubIntegration";
    private readonly IDataProtector stateProtector = protection.CreateProtector("DevHub.GitHub.OAuthState.v1");
    private sealed record Flow(Guid UserId, string State, string Verifier, DateTimeOffset Until, bool Installation = false);

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct) => TryGetAuthenticatedUserId(out var userId)
        ? Ok(await github.Status(userId, ct)) : InvalidAuthenticatedUser();

    [HttpPost("authorize")]
    public IActionResult AuthorizeRepositories([FromQuery] bool install = false)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        if (!github.Configured) return Problem(statusCode: 503, title: "GitHub integration is not configured.");
        return Ok(new {url = BeginFlow(userId, install)});
    }

    private string BeginFlow(Guid userId, bool install)
    {
        var state = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var verifier = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var until = clock.GetUtcNow().AddMinutes(10);
        Response.Cookies.Append(FlowCookie, stateProtector.Protect(JsonSerializer.Serialize(new Flow(userId, state, verifier, until, install))),
            new CookieOptions {HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = until, IsEssential = true});
        return install ? QueryHelpers.AddQueryString(github.InstallUrl, "state", state) : QueryHelpers.AddQueryString("https://github.com/login/oauth/authorize", new Dictionary<string, string?> {
            ["client_id"] = github.ClientId, ["redirect_uri"] = github.CallbackUrl, ["state"] = state,
            ["code_challenge"] = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256", ["scope"] = "offline_access"
        });
    }

    [AllowAnonymous]
    [HttpGet("callback")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        var success = false;
        var cookie = Request.Cookies[FlowCookie];
        Response.Cookies.Delete(FlowCookie, new CookieOptions {Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/"});
        try {
            var session = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var subject = session.Principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? session.Principal?.FindFirstValue("sub");
            var flow = cookie is null ? null : JsonSerializer.Deserialize<Flow>(stateProtector.Unprotect(cookie));
            if (flow is not null && session.Succeeded && Guid.TryParse(subject, out var userId) && userId == flow.UserId
                && flow.Until > clock.GetUtcNow() && state is not null && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(state), Encoding.UTF8.GetBytes(flow.State))
                && error is null)
            {
                // Installation redirects do not bind their code to a PKCE challenge.
                // Start a fresh authorization before exchanging any user credential.
                if (flow.Installation) return Redirect(BeginFlow(userId, false));
                if (!string.IsNullOrWhiteSpace(code) && !string.IsNullOrWhiteSpace(flow.Verifier))
                {
                    await github.Connect(userId, code, flow.Verifier, ct);
                    success = true;
                }
            }
        } catch (Exception e) when (e is CryptographicException or JsonException or GitHubIntegrationException or HttpRequestException or TaskCanceledException) { }
        var frontend = (config["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
        return Redirect($"{frontend}/github-connected?status={(success ? "connected" : "failed")}");
    }

    [HttpGet("installations")]
    public async Task<IActionResult> Installations(CancellationToken ct) => TryGetAuthenticatedUserId(out var userId)
        ? Ok(await github.Installations(userId, ct)) : InvalidAuthenticatedUser();

    [HttpGet("installations/{installationId:long}/repositories")]
    public async Task<IActionResult> Repositories(long installationId, [FromQuery] int page = 1, CancellationToken ct = default) => TryGetAuthenticatedUserId(out var userId)
        ? Ok(await github.Repositories(userId, installationId, page, ct)) : InvalidAuthenticatedUser();

    [HttpDelete("connection")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        await github.Disconnect(userId, ct);
        return NoContent();
    }
}
