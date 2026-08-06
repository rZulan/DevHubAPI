using ProjectRoadMapper.Application.Authentication;

namespace ProjectRoadMapper.Application.Abstractions.Authentication;

public interface IRefreshTokenService
{
    RefreshTokenValue Generate();

    string Hash(string token);
}
