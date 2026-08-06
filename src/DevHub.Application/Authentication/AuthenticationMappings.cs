using DevHub.Domain.Users;

namespace DevHub.Application.Authentication;

internal static class AuthenticationMappings
{
    public static AuthenticationResponse ToAuthenticationResponse(
        this User user,
        TokenResult accessToken,
        RefreshTokenValue refreshToken) =>
        new(
            user.Id,
            user.Email,
            user.FirstName,
            user.LastName,
            accessToken.AccessToken,
            accessToken.ExpiresAtUtc,
            refreshToken.Token,
            refreshToken.ExpiresAtUtc);
}
