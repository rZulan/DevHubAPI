using MediatR;
using DevHub.Application.Abstractions.Authentication;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Users;
using DevHub.Domain.Authentication;
using DevHub.Domain.Users;

namespace DevHub.Application.Authentication.ExternalAuthentication;

internal sealed class ExternalSignInCommandHandler(
    IExternalAccountRepository externalAccountRepository,
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService,
    TimeProvider timeProvider)
    : IRequestHandler<ExternalSignInCommand, Result<AuthenticationResponse>>
{
    public async Task<Result<AuthenticationResponse>> Handle(
        ExternalSignInCommand request,
        CancellationToken cancellationToken)
    {
        var identity = request.Identity;
        var now = timeProvider.GetUtcNow();
        var externalAccount = await externalAccountRepository.GetByProviderIdentityAsync(
            identity.Provider,
            identity.ProviderUserId,
            cancellationToken);
        User user;

        if (externalAccount is not null)
        {
            user = await userRepository.GetByIdAsync(externalAccount.UserId, cancellationToken)
                ?? throw new InvalidOperationException("An external account references a missing user.");

            externalAccount.UpdateProviderProfile(
                identity.ProviderUsername,
                identity.Email,
                identity.AvatarUrl,
                now);
        }
        else
        {
            if (await userRepository.GetByEmailAsync(identity.Email, cancellationToken) is not null)
            {
                return Result<AuthenticationResponse>.Failure(
                    AuthenticationErrors.ExistingAccountMustBeLinked);
            }

            var preferredUsername = identity.ProviderUsername ?? identity.Email.Split('@', 2)[0];
            var username = await UsernamePolicy.CreateAvailableAsync(
                preferredUsername,
                userRepository,
                cancellationToken);

            user = User.CreateExternal(
                identity.Email,
                username,
                identity.FirstName,
                identity.LastName,
                identity.AvatarUrl,
                now);

            externalAccount = ExternalAccount.Create(
                user.Id,
                identity.Provider,
                identity.ProviderUserId,
                identity.ProviderUsername,
                identity.Email,
                identity.AvatarUrl,
                now);

            user.AddExternalAccount(externalAccount);
            userRepository.Add(user);
        }

        var refreshTokenValue = refreshTokenService.Generate();
        refreshTokenRepository.Add(RefreshToken.Create(
            user.Id,
            refreshTokenValue.TokenHash,
            refreshTokenValue.CreatedAtUtc,
            refreshTokenValue.ExpiresAtUtc));

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<AuthenticationResponse>.Failure(
                AuthenticationErrors.ExternalAccountAlreadyLinked);
        }

        var accessToken = jwtTokenGenerator.Generate(user);
        return Result<AuthenticationResponse>.Success(
            user.ToAuthenticationResponse(accessToken, refreshTokenValue));
    }
}
