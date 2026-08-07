using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using DevHub.Application.Authentication;
using DevHub.Application.Authentication.Commands.Login;
using DevHub.Application.Authentication.Commands.Logout;
using DevHub.Application.Authentication.Commands.RefreshAccessToken;
using DevHub.Application.Authentication.Commands.RegisterUser;
using DevHub.Application.Authentication.ExternalAuthentication;
using DevHub.Infrastructure.Authentication;

namespace DevHub.Api.Controllers;

/// <summary>
/// Creates and manages authenticated user sessions.
/// </summary>
[Tags("Authentication")]
[Route("api/auth")]
public sealed class AuthController(
    ISender sender,
    IOptions<AuthenticationCookieOptions> cookieOptions,
    IConfiguration configuration) : ApiControllerBase
{
    /// <summary>
    /// Register
    /// </summary>
    /// <remarks>
    /// Creates a user account, returns a JWT access/refresh token pair, and establishes
    /// a secure HTTP-only authentication cookie.
    /// </remarks>
    /// <returns>The new user's profile and authentication tokens.</returns>
    /// <response code="201">The user was registered and signed in.</response>
    /// <response code="400">One or more registration fields are invalid.</response>
    /// <response code="409">An account already uses the supplied email address.</response>
    [HttpPost("register")]
    [EndpointName("RegisterUser")]
    [AllowAnonymous]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Register(
        [FromBody]
        RegisterUserCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        await SignInWithCookieAsync(result.Value!);
        return StatusCode(StatusCodes.Status201Created, result.Value);
    }

    /// <summary>
    /// Login
    /// </summary>
    /// <remarks>
    /// Returns a JWT access/refresh token pair and establishes a secure HTTP-only
    /// authentication cookie. Invalid credentials always return the same response.
    /// </remarks>
    /// <returns>The authenticated user's profile and authentication tokens.</returns>
    /// <response code="200">The credentials are valid and the user was signed in.</response>
    /// <response code="400">The request body could not be validated.</response>
    /// <response code="401">The email address or password is invalid.</response>
    [HttpPost("login")]
    [EndpointName("LoginUser")]
    [AllowAnonymous]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(
        [FromBody]
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        await SignInWithCookieAsync(result.Value!);
        return Ok(result.Value);
    }

    /// <summary>
    /// Refresh
    /// </summary>
    /// <remarks>
    /// Exchanges an active refresh token for a new access/refresh token pair and updates
    /// the authentication cookie. Reusing a revoked token invalidates its token family.
    /// </remarks>
    /// <returns>The authenticated user's profile and the rotated token pair.</returns>
    /// <response code="200">The refresh token was rotated successfully.</response>
    /// <response code="400">The request body could not be validated.</response>
    /// <response code="401">The refresh token is invalid, expired, or revoked.</response>
    [HttpPost("refresh")]
    [EndpointName("RefreshAccessToken")]
    [AllowAnonymous]
    [ProducesResponseType<AuthenticationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        RefreshAccessTokenCommand? command,
        CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[cookieOptions.Value.RefreshCookieName]
            ?? command?.RefreshToken;
        var result = await sender.Send(
            new RefreshAccessTokenCommand(refreshToken ?? string.Empty),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return Failure(result.Error!);
        }

        await SignInWithCookieAsync(result.Value!);
        return Ok(result.Value);
    }

    /// <summary>
    /// Logout
    /// </summary>
    /// <remarks>
    /// Clears the authentication cookie. This operation does not revoke previously issued
    /// JWT access tokens or refresh tokens.
    /// </remarks>
    /// <returns>No response body.</returns>
    /// <response code="204">The authentication cookie was cleared.</response>
    /// <response code="401">The request is not authenticated.</response>
    [HttpPost("logout")]
    [EndpointName("LogoutUser")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        await sender.Send(
            new LogoutCommand(
                userId.Value,
                Request.Cookies[cookieOptions.Value.RefreshCookieName]),
            cancellationToken);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        DeleteRefreshTokenCookie();
        return NoContent();
    }

    /// <summary>
    /// Start external authentication with Google or GitHub.
    /// </summary>
    [HttpGet("external/{provider}")]
    [AllowAnonymous]
    public IActionResult External(
        string provider,
        [FromQuery] string intent = "login",
        [FromQuery] string returnUrl = "/account/profile")
    {
        var scheme = GetProviderScheme(provider);

        if (scheme is null)
        {
            return NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "OAuth provider not supported",
                Detail = "Supported providers are google and github."
            });
        }

        if (!ProviderIsConfigured(provider))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "OAuth provider not configured",
                detail: $"The {provider.ToLowerInvariant()} OAuth credentials are not configured.");
        }

        if (intent is not ("login" or "register"))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid OAuth intent",
                Detail = "Intent must be either login or register."
            });
        }

        if (!IsAllowedReturnUrl(returnUrl))
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Invalid return URL",
                Detail = "The requested return URL is not allowed."
            });
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(ExternalCallback))
        };
        properties.Items[ExternalAuthenticationConstants.IntentItem] = intent;
        properties.Items[ExternalAuthenticationConstants.ProviderItem] = provider.ToLowerInvariant();
        properties.Items[ExternalAuthenticationConstants.ReturnUrlItem] = returnUrl;

        return Challenge(properties, scheme);
    }

    /// <summary>
    /// Complete external authentication after the provider callback.
    /// </summary>
    [HttpGet("external/callback")]
    [AllowAnonymous]
    [ApiExplorerSettings(IgnoreApi = true)]
    public async Task<IActionResult> ExternalCallback(CancellationToken cancellationToken)
    {
        var externalResult = await HttpContext.AuthenticateAsync(
            AuthenticationSchemes.ExternalCookie);
        var returnUrl = GetAuthenticationItem(
            externalResult.Properties,
            ExternalAuthenticationConstants.ReturnUrlItem) ?? "/account/profile";

        if (!externalResult.Succeeded || externalResult.Principal is null)
        {
            return RedirectToFrontend(returnUrl, "external_authentication_failed");
        }

        var provider = GetAuthenticationItem(
            externalResult.Properties,
            ExternalAuthenticationConstants.ProviderItem);
        var identity = CreateExternalIdentity(provider, externalResult.Principal);

        if (identity is null)
        {
            await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
            return RedirectToFrontend(returnUrl, "verified_email_required");
        }

        var linkingUserIdValue = GetAuthenticationItem(
            externalResult.Properties,
            ExternalAuthenticationConstants.LinkingUserIdItem);

        if (Guid.TryParse(linkingUserIdValue, out var linkingUserId))
        {
            var currentSession = await HttpContext.AuthenticateAsync(
                CookieAuthenticationDefaults.AuthenticationScheme);
            var currentUserId = currentSession.Principal?.FindFirstValue(
                ClaimTypes.NameIdentifier);

            if (!currentSession.Succeeded ||
                !Guid.TryParse(currentUserId, out var authenticatedUserId) ||
                authenticatedUserId != linkingUserId)
            {
                await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);
                return RedirectToFrontend(returnUrl, "linking_session_expired");
            }

            var linkResult = await sender.Send(
                new LinkExternalAccountCommand(linkingUserId, identity),
                cancellationToken);
            await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);

            return linkResult.IsSuccess
                ? RedirectToFrontend(returnUrl)
                : RedirectToFrontend(returnUrl, linkResult.Error!.Code);
        }

        var result = await sender.Send(new ExternalSignInCommand(identity), cancellationToken);
        await HttpContext.SignOutAsync(AuthenticationSchemes.ExternalCookie);

        if (!result.IsSuccess)
        {
            return RedirectToFrontend(returnUrl, result.Error!.Code);
        }

        await SignInWithCookieAsync(result.Value!);
        return RedirectToFrontend(returnUrl);
    }

    private Task SignInWithCookieAsync(AuthenticationResponse response)
    {
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, response.UserId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, response.UserId.ToString()),
            new Claim(ClaimTypes.Email, response.Email),
            new Claim(ClaimTypes.Name, $"{response.FirstName} {response.LastName}".Trim())
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        var properties = new AuthenticationProperties
        {
            IsPersistent = true,
            AllowRefresh = true,
            ExpiresUtc = response.RefreshTokenExpiresAtUtc
        };

        WriteRefreshTokenCookie(response);

        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
    }

    private void WriteRefreshTokenCookie(AuthenticationResponse response)
    {
        var settings = cookieOptions.Value;

        Response.Cookies.Append(
            settings.RefreshCookieName,
            response.RefreshToken,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = settings.Secure,
                SameSite = settings.GetSameSiteMode(),
                Path = "/api/auth",
                Expires = response.RefreshTokenExpiresAtUtc,
                IsEssential = true
            });
    }

    private void DeleteRefreshTokenCookie()
    {
        var settings = cookieOptions.Value;

        Response.Cookies.Delete(
            settings.RefreshCookieName,
            new CookieOptions
            {
                HttpOnly = true,
                Secure = settings.Secure,
                SameSite = settings.GetSameSiteMode(),
                Path = "/api/auth"
            });
    }

    private Guid? GetAuthenticatedUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }

    private string? GetProviderScheme(string provider) =>
        provider.ToLowerInvariant() switch
        {
            "google" => AuthenticationSchemes.Google,
            "github" => AuthenticationSchemes.GitHub,
            _ => null
        };

    private bool ProviderIsConfigured(string provider)
    {
        var sectionName = provider.ToLowerInvariant() switch
        {
            "google" => "Google",
            "github" => "GitHub",
            _ => provider
        };

        return !string.IsNullOrWhiteSpace(
                   configuration[$"Authentication:{sectionName}:ClientId"]) &&
               !string.IsNullOrWhiteSpace(
                   configuration[$"Authentication:{sectionName}:ClientSecret"]);
    }

    private bool IsAllowedReturnUrl(string returnUrl)
    {
        var allowedPaths = configuration
            .GetSection("Frontend:AllowedReturnPaths")
            .Get<string[]>() ?? ["/account/profile", "/account/account"];

        return returnUrl.StartsWith("/", StringComparison.Ordinal) &&
               !returnUrl.StartsWith("//", StringComparison.Ordinal) &&
               allowedPaths.Contains(returnUrl, StringComparer.Ordinal);
    }

    private static string? GetAuthenticationItem(
        AuthenticationProperties? properties,
        string key) =>
        properties is not null && properties.Items.TryGetValue(key, out var value)
            ? value
            : null;

    private IActionResult RedirectToFrontend(string returnUrl, string? error = null)
    {
        if (!IsAllowedReturnUrl(returnUrl))
        {
            returnUrl = "/account/profile";
        }

        var frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
        var destination = new Uri(new Uri(frontendBaseUrl), returnUrl).ToString();

        if (!string.IsNullOrWhiteSpace(error))
        {
            destination = QueryHelpers.AddQueryString(destination, "oauthError", error);
        }

        return Redirect(destination);
    }

    private static ExternalIdentity? CreateExternalIdentity(
        string? provider,
        ClaimsPrincipal principal)
    {
        var providerUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var emailVerified = principal.FindFirstValue(
            ExternalAuthenticationConstants.EmailVerifiedClaim);

        if (string.IsNullOrWhiteSpace(provider) ||
            string.IsNullOrWhiteSpace(providerUserId) ||
            string.IsNullOrWhiteSpace(email) ||
            !bool.TryParse(emailVerified, out var isVerified) ||
            !isVerified)
        {
            return null;
        }

        var providerUsername = principal.FindFirstValue(
            ExternalAuthenticationConstants.ProviderUsernameClaim);
        var displayName = principal.FindFirstValue(ClaimTypes.Name)
            ?? providerUsername
            ?? email.Split('@', 2)[0];
        var givenName = principal.FindFirstValue(ClaimTypes.GivenName);
        var surname = principal.FindFirstValue(ClaimTypes.Surname);
        var nameParts = displayName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = givenName ?? nameParts.FirstOrDefault() ?? "DevHub";
        var lastName = surname ?? (nameParts.Length > 1 ? nameParts[1] : "User");

        return new ExternalIdentity(
            provider,
            providerUserId,
            email,
            providerUsername,
            firstName,
            lastName,
            principal.FindFirstValue(ExternalAuthenticationConstants.AvatarUrlClaim));
    }

}
