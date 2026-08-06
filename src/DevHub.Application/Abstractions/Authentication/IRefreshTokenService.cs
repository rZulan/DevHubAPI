using DevHub.Application.Authentication;

namespace DevHub.Application.Abstractions.Authentication;

public interface IRefreshTokenService
{
    RefreshTokenValue Generate();

    string Hash(string token);
}
