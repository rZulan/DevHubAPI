using MediatR;
using ProjectRoadMapper.Application.Abstractions.Authentication;
using ProjectRoadMapper.Application.Abstractions.Persistence;
using ProjectRoadMapper.Application.Common;
using ProjectRoadMapper.Domain.Authentication;

namespace ProjectRoadMapper.Application.Authentication.Commands.Login;

internal sealed class LoginCommandHandler(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService)
    : IRequestHandler<LoginCommand, Result<AuthenticationResponse>>
{
    public async Task<Result<AuthenticationResponse>> Handle(
        LoginCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.InvalidCredentials);
        }

        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);

        if (user is null || !passwordHasher.Verify(user.PasswordHash, request.Password))
        {
            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.InvalidCredentials);
        }

        var refreshTokenValue = refreshTokenService.Generate();
        refreshTokenRepository.Add(RefreshToken.Create(
            user.Id,
            refreshTokenValue.TokenHash,
            refreshTokenValue.CreatedAtUtc,
            refreshTokenValue.ExpiresAtUtc));

        await unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = jwtTokenGenerator.Generate(user);
        return Result<AuthenticationResponse>.Success(
            user.ToAuthenticationResponse(accessToken, refreshTokenValue));
    }
}
