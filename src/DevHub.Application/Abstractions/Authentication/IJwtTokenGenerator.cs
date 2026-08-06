using DevHub.Application.Authentication;
using DevHub.Domain.Users;

namespace DevHub.Application.Abstractions.Authentication;

public interface IJwtTokenGenerator
{
    TokenResult Generate(User user);
}
