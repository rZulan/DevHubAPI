using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using DevHub.Application.Common;
using DevHub.Application.Authentication.ExternalAuthentication;
using DevHub.Application.Users;
using DevHub.Application.Users.Commands.UpdateCurrentUser;
using DevHub.Application.Users.Queries.GetUserById;
using DevHub.Infrastructure.Authentication;

namespace DevHub.Api.Controllers;

/// <summary>
/// Retrieves user profile information.
/// </summary>
[Tags("Users")]
[Route("api/users")]
[Authorize]
public sealed class UsersController(
    ISender sender,
    IConfiguration configuration) : ApiControllerBase
{
    /// <summary>
    /// Profile
    /// </summary>
    /// <remarks>
    /// Resolves the user from the authenticated cookie or JWT bearer token and returns
    /// the corresponding profile.
    /// </remarks>
    /// <returns>The authenticated user's profile.</returns>
    /// <response code="200">The current user's profile was found.</response>
    /// <response code="401">The request is unauthenticated or contains an invalid user identifier.</response>
    /// <response code="404">The authenticated user no longer exists.</response>
    [HttpGet("me")]
    [EndpointName("GetCurrentUser")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();

        if (userId is null)
        {
            return Failure(new Error(
                "Authentication.InvalidSubject",
                "The authenticated identity does not contain a valid user identifier.",
                ErrorType.Unauthorized));
        }

        var result = await sender.Send(new GetUserByIdQuery(userId.Value), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Failure(result.Error!);
    }

    /// <summary>
    /// Update the current user's public profile.
    /// </summary>
    [HttpPatch("me")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateCurrentUser(
        [FromBody] UpdateCurrentUserRequest request,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        var result = await sender.Send(
            new UpdateCurrentUserCommand(
                userId.Value,
                request.Username,
                request.FirstName,
                request.LastName),
            cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Failure(result.Error!);
    }

    /// <summary>
    /// Start a provider flow that links an external identity to the current account.
    /// </summary>
    [HttpGet("me/connections/{provider}")]
    public IActionResult ConnectExternalAccount(
        string provider,
        [FromQuery] string returnUrl = "/account/account")
    {
        var userId = GetAuthenticatedUserId();
        var scheme = GetProviderScheme(provider);

        if (userId is null)
        {
            return Unauthorized();
        }

        if (scheme is null)
        {
            return NotFound();
        }

        if (!ProviderIsConfigured(provider))
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "OAuth provider not configured",
                detail: $"The {provider.ToLowerInvariant()} OAuth credentials are not configured.");
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
            RedirectUri = Url.Action(nameof(AuthController.ExternalCallback), "Auth")
        };
        properties.Items[ExternalAuthenticationConstants.IntentItem] = "link";
        properties.Items[ExternalAuthenticationConstants.ProviderItem] = provider.ToLowerInvariant();
        properties.Items[ExternalAuthenticationConstants.ReturnUrlItem] = returnUrl;
        properties.Items[ExternalAuthenticationConstants.LinkingUserIdItem] = userId.Value.ToString();

        return Challenge(properties, scheme);
    }

    /// <summary>
    /// Disconnect an external provider while preserving at least one sign-in method.
    /// </summary>
    [HttpDelete("me/connections/{provider}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DisconnectExternalAccount(
        string provider,
        CancellationToken cancellationToken)
    {
        var userId = GetAuthenticatedUserId();

        if (userId is null)
        {
            return Unauthorized();
        }

        if (GetProviderScheme(provider) is null)
        {
            return NotFound();
        }

        var result = await sender.Send(
            new DisconnectExternalAccountCommand(userId.Value, provider),
            cancellationToken);

        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }

    private Guid? GetAuthenticatedUserId()
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(subject, out var userId) ? userId : null;
    }

    private static string? GetProviderScheme(string provider) =>
        provider.ToLowerInvariant() switch
        {
            "google" => AuthenticationSchemes.Google,
            "github" => AuthenticationSchemes.GitHub,
            _ => null
        };

    private bool ProviderIsConfigured(string provider)
    {
        var sectionName = provider.Equals("github", StringComparison.OrdinalIgnoreCase)
            ? "GitHub"
            : "Google";

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
}

public sealed record UpdateCurrentUserRequest(
    string Username,
    string FirstName,
    string LastName);
