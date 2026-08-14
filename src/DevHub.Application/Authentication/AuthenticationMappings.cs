using DevHub.Domain.Users;
using DevHub.Application.Users;

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
            user.Username,
            user.FirstName,
            user.LastName,
            user.DateOfBirth,
            user.AvatarUrl,
            user.ExternalAccounts
                .OrderBy(account => account.Provider)
                .Select(account => new ConnectedAccountResponse(
                    account.Provider,
                    account.ProviderUsername))
                .ToArray(),
            accessToken.AccessToken,
            accessToken.ExpiresAtUtc,
            refreshToken.Token,
            refreshToken.ExpiresAtUtc);
}
