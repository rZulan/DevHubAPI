using MediatR;
using DevHub.Application.Abstractions.Authentication;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Domain.Authentication;
using DevHub.Domain.Users;

namespace DevHub.Application.Authentication.Commands.RegisterUser;

internal sealed class RegisterUserCommandHandler(
    IUserRepository userRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IJwtTokenGenerator jwtTokenGenerator,
    IRefreshTokenService refreshTokenService,
    TimeProvider timeProvider)
    : IRequestHandler<RegisterUserCommand, Result<AuthenticationResponse>>
{
    public async Task<Result<AuthenticationResponse>> Handle(
        RegisterUserCommand request,
        CancellationToken cancellationToken)
    {
        var validationErrors = RequestValidation.Validate(request);

        if (validationErrors.Count > 0)
        {
            return Result<AuthenticationResponse>.Failure(
                AuthenticationErrors.InvalidRegistration(validationErrors));
        }

        if (await userRepository.GetByEmailAsync(request.Email, cancellationToken) is not null)
        {
            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.EmailAlreadyExists);
        }

        var passwordHash = passwordHasher.Hash(request.Password);
        var user = User.Create(
            request.Email,
            request.FirstName,
            request.LastName,
            passwordHash,
            timeProvider.GetUtcNow());

        userRepository.Add(user);
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
            return Result<AuthenticationResponse>.Failure(AuthenticationErrors.EmailAlreadyExists);
        }

        var accessToken = jwtTokenGenerator.Generate(user);
        return Result<AuthenticationResponse>.Success(
            user.ToAuthenticationResponse(accessToken, refreshTokenValue));
    }
}
