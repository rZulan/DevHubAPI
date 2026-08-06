using MediatR;
using DevHub.Application.Common;

namespace DevHub.Application.Authentication.Commands.Login;

/// <summary>
/// Credentials used to authenticate a user.
/// </summary>
/// <param name="Email">The account email address.</param>
/// <param name="Password">The account password.</param>
public sealed record LoginCommand(string Email, string Password)
    : IRequest<Result<AuthenticationResponse>>;
