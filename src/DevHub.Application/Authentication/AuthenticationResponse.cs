using System.Text.Json.Serialization;
using DevHub.Application.Users;

namespace DevHub.Application.Authentication;

/// <summary>
/// The authenticated user's profile and newly issued access token.
/// </summary>
/// <param name="UserId">The user's unique identifier.</param>
/// <param name="Email">The user's email address.</param>
/// <param name="Username">The user's public username.</param>
/// <param name="FirstName">The user's given name.</param>
/// <param name="LastName">The user's family name.</param>
/// <param name="AvatarUrl">The user's optional avatar URL.</param>
/// <param name="ConnectedAccounts">The external accounts linked to the user.</param>
/// <param name="AccessToken">A JWT used to authorize API requests.</param>
/// <param name="AccessTokenExpiresAtUtc">The UTC expiration time of the access token.</param>
/// <param name="RefreshToken">An internal opaque token written only to an HTTP-only cookie.</param>
/// <param name="RefreshTokenExpiresAtUtc">The UTC expiration time of the refresh token.</param>
public sealed record AuthenticationResponse(
    Guid UserId,
    string Email,
    string Username,
    string FirstName,
    string LastName,
    string? AvatarUrl,
    IReadOnlyCollection<ConnectedAccountResponse> ConnectedAccounts,
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    [property: JsonIgnore]
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);
