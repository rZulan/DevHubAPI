using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DevHub.Application.Authentication;
using DevHub.Application.Authentication.Commands.Login;
using DevHub.Application.Authentication.Commands.RefreshAccessToken;
using DevHub.Application.Authentication.Commands.RegisterUser;

namespace DevHub.Api.Controllers;

/// <summary>
/// Creates and manages authenticated user sessions.
/// </summary>
[Tags("Authentication")]
[Route("api/auth")]
public sealed class AuthController(ISender sender) : ApiControllerBase
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
        [FromBody]
        RefreshAccessTokenCommand command,
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
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return NoContent();
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

        return HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            properties);
    }

}
