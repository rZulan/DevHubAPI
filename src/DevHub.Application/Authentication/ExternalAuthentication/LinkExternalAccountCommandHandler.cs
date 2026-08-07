using MediatR;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Users;
using DevHub.Domain.Authentication;

namespace DevHub.Application.Authentication.ExternalAuthentication;

internal sealed class LinkExternalAccountCommandHandler(
    IExternalAccountRepository externalAccountRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<LinkExternalAccountCommand, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> Handle(
        LinkExternalAccountCommand request,
        CancellationToken cancellationToken)
    {
        var identity = request.Identity;
        var providerIdentity = await externalAccountRepository.GetByProviderIdentityAsync(
            identity.Provider,
            identity.ProviderUserId,
            cancellationToken);

        if (providerIdentity is not null)
        {
            return providerIdentity.UserId == request.UserId
                ? await GetUserResultAsync(request.UserId, cancellationToken)
                : Result<UserResponse>.Failure(AuthenticationErrors.ExternalAccountAlreadyLinked);
        }

        if (await externalAccountRepository.GetByUserAndProviderAsync(
                request.UserId,
                identity.Provider,
                cancellationToken) is not null)
        {
            return Result<UserResponse>.Failure(AuthenticationErrors.ProviderAlreadyLinked);
        }

        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user is null)
        {
            return Result<UserResponse>.Failure(new Error(
                "Users.NotFound",
                "The requested user was not found.",
                ErrorType.NotFound));
        }

        externalAccountRepository.Add(ExternalAccount.Create(
            user.Id,
            identity.Provider,
            identity.ProviderUserId,
            identity.ProviderUsername,
            identity.Email,
            identity.AvatarUrl,
            timeProvider.GetUtcNow()));

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<UserResponse>.Failure(AuthenticationErrors.ExternalAccountAlreadyLinked);
        }

        return await GetUserResultAsync(request.UserId, cancellationToken);
    }

    private async Task<Result<UserResponse>> GetUserResultAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);

        return user is null
            ? Result<UserResponse>.Failure(new Error(
                "Users.NotFound",
                "The requested user was not found.",
                ErrorType.NotFound))
            : Result<UserResponse>.Success(user.ToUserResponse());
    }
}
