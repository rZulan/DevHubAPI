using MediatR;
using DevHub.Application.Abstractions.Authentication;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;

namespace DevHub.Application.Authentication.Commands.Logout;

internal sealed class LogoutCommandHandler(
    IRefreshTokenRepository refreshTokenRepository,
    IRefreshTokenService refreshTokenService,
    TimeProvider timeProvider)
    : IRequestHandler<LogoutCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        LogoutCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            return Result<bool>.Success(true);
        }

        var token = await refreshTokenRepository.GetByHashAsync(
            refreshTokenService.Hash(request.RefreshToken),
            cancellationToken);

        if (token is not null && token.UserId == request.UserId)
        {
            await refreshTokenRepository.RevokeFamilyAsync(
                token.FamilyId,
                timeProvider.GetUtcNow(),
                cancellationToken);
        }

        return Result<bool>.Success(true);
    }
}
