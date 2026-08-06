using ProjectRoadMapper.Application.Authentication;
using ProjectRoadMapper.Domain.Users;

namespace ProjectRoadMapper.Application.Abstractions.Authentication;

public interface IJwtTokenGenerator
{
    TokenResult Generate(User user);
}
