using MediatR;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;

namespace DevHub.Application.Authentication.ExternalAuthentication;

internal sealed class DisconnectExternalAccountCommandHandler(
    IExternalAccountRepository externalAccountRepository,
    IUserRepository userRepository,
    IUnitOfWork unitOfWork)
    : IRequestHandler<DisconnectExternalAccountCommand, Result<bool>>
{
    public async Task<Result<bool>> Handle(
        DisconnectExternalAccountCommand request,
        CancellationToken cancellationToken)
    {
        var user = await userRepository.GetByIdAsync(request.UserId, cancellationToken);

        if (user is null)
        {
            return Result<bool>.Failure(new Error(
                "Users.NotFound",
                "The requested user was not found.",
                ErrorType.NotFound));
        }

        var account = await externalAccountRepository.GetByUserAndProviderAsync(
            request.UserId,
            request.Provider,
            cancellationToken);

        if (account is null)
        {
            return Result<bool>.Failure(new Error(
                "Authentication.ExternalAccountNotFound",
                "The external account connection was not found.",
                ErrorType.NotFound));
        }

        if (!user.HasPassword && user.ExternalAccounts.Count <= 1)
        {
            return Result<bool>.Failure(AuthenticationErrors.FinalSignInMethod);
        }

        externalAccountRepository.Remove(account);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}
