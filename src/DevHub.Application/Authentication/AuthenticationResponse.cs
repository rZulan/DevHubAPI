namespace DevHub.Application.Authentication;

/// <summary>
/// The authenticated user's profile and newly issued token pair.
/// </summary>
/// <param name="UserId">The user's unique identifier.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="FirstName">The user's given name.</param>
/// <param name="LastName">The user's family name.</param>
/// <param name="AccessToken">A JWT used to authorize API requests.</param>
/// <param name="AccessTokenExpiresAtUtc">The UTC expiration time of the access token.</param>
/// <param name="RefreshToken">An opaque, single-use token used to obtain another token pair.</param>
/// <param name="RefreshTokenExpiresAtUtc">The UTC expiration time of the refresh token.</param>
public sealed record AuthenticationResponse(
    Guid UserId,
    string Email,
    string FirstName,
    string LastName,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);
