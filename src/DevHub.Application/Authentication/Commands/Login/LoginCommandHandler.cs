using MediatR;
using DevHub.Application.Abstractions.Authentication;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Domain.Authentication;

namespace DevHub.Application.Authentication.Commands.Login;

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
            request.Email.Length > 320 || string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 128)
        {
            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.InvalidCredentials);
        }

        var user = await userRepository.GetByEmailAsync(request.Email, cancellationToken);

        // Missing/passwordless accounts still perform a password hash verification.
        var verified = passwordHasher.Verify(user?.PasswordHash ?? string.Empty, request.Password);
        if (user is null || !user.HasPassword || !verified)
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
