using MediatR;
using DevHub.Application.Common;

namespace DevHub.Application.Users.Commands.UpdateCurrentUser;

public sealed record UpdateCurrentUserCommand(
    Guid UserId,
    string Username,
    string FirstName,
    string LastName,
    DateOnly? DateOfBirth) : IRequest<Result<UserResponse>>;
