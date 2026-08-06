using MediatR;
using ProjectRoadMapper.Application.Abstractions.Authentication;
using ProjectRoadMapper.Application.Abstractions.Persistence;
using ProjectRoadMapper.Application.Common;
using ProjectRoadMapper.Domain.Authentication;

namespace ProjectRoadMapper.Application.Authentication.Commands.RefreshAccessToken;

internal sealed class RefreshAccessTokenCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService,
    TimeProvider timeProvider)
    : IRequestHandler<RefreshAccessTokenCommand, Result<AuthenticationResponse>>
{
    private const int MaximumRefreshTokenLength = 512;

    public async Task<Result<AuthenticationResponse>> Handle(
        RefreshAccessTokenCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken) ||
            request.RefreshToken.Length > MaximumRefreshTokenLength)
        {
            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.InvalidRefreshToken);
        }

        var tokenHash = refreshTokenService.Hash(request.RefreshToken);
        var currentToken = await refreshTokenRepository.GetByHashAsync(
            tokenHash,
            cancellationToken);

        if (currentToken is null)
        {
            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.InvalidRefreshToken);
        }

        var now = timeProvider.GetUtcNow();

        if (currentToken.RevokedAtUtc is not null)
        {
            await refreshTokenRepository.RevokeFamilyAsync(
                currentToken.FamilyId,
                now,
                cancellationToken);

            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.InvalidRefreshToken);
        }

        if (!currentToken.IsActive(now))
        {
            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.InvalidRefreshToken);
        }

        var user = await userRepository.GetByIdAsync(currentToken.UserId, cancellationToken);

        if (user is null)
        {
            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.InvalidRefreshToken);
        }

        var refreshTokenValue = refreshTokenService.Generate();
        var replacementToken = RefreshToken.Create(
            user.Id,
            refreshTokenValue.TokenHash,
            refreshTokenValue.CreatedAtUtc,
            refreshTokenValue.ExpiresAtUtc,
            currentToken.FamilyId);

        currentToken.Rotate(replacementToken.Id, now);
        refreshTokenRepository.Add(replacementToken);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (PersistenceConcurrencyException)
        {
            await refreshTokenRepository.RevokeFamilyAsync(
                currentToken.FamilyId,
                now,
                cancellationToken);

            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.InvalidRefreshToken);
        }

        var accessToken = jwtTokenGenerator.Generate(user);
        return Result<AuthenticationResponse>.Success(
            user.ToAuthenticationResponse(accessToken, refreshTokenValue));
    }
}
