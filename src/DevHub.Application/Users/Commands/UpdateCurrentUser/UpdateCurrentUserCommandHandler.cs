using MediatR;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Authentication;

namespace DevHub.Application.Users.Commands.UpdateCurrentUser;

internal sealed class UpdateCurrentUserCommandHandler(
    IUserRepository userRepository,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateCurrentUserCommand, Result<UserResponse>>
{
    public async Task<Result<UserResponse>> Handle(
        UpdateCurrentUserCommand request,
        CancellationToken cancellationToken)
    {
        var validationErrors = new Dictionary<string, string[]>();
        var usernameError = UsernamePolicy.GetValidationError(request.Username);

        if (usernameError is not null)
        {
            validationErrors[nameof(request.Username)] = [usernameError];
        }

        if (string.IsNullOrWhiteSpace(request.FirstName) || request.FirstName.Length > 100)
        {
            validationErrors[nameof(request.FirstName)] =
                ["First name is required and must not exceed 100 characters."];
        }

        if (string.IsNullOrWhiteSpace(request.LastName) || request.LastName.Length > 100)
        {
            validationErrors[nameof(request.LastName)] =
                ["Last name is required and must not exceed 100 characters."];
        }

        if (validationErrors.Count > 0)
        {
            return Result<UserResponse>.Failure(new Error(
                "Users.InvalidProfile",
                "One or more profile fields are invalid.",
                ErrorType.Validation,
                validationErrors));
        }

        if (await userRepository.UsernameExistsAsync(
                request.Username,
                request.UserId,
                cancellationToken))
        {
            return Result<UserResponse>.Failure(AuthenticationErrors.UsernameAlreadyExists);
        }

        var user = await userRepository.GetByIdForUpdateAsync(request.UserId, cancellationToken);

        if (user is null)
        {
            return Result<UserResponse>.Failure(new Error(
                "Users.NotFound",
                "The requested user was not found.",
                ErrorType.NotFound));
        }

        user.UpdateProfile(
            request.Username,
            request.FirstName,
            request.LastName,
            timeProvider.GetUtcNow());

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<UserResponse>.Failure(AuthenticationErrors.UsernameAlreadyExists);
        }

        return Result<UserResponse>.Success(user.ToUserResponse());
    }
}
