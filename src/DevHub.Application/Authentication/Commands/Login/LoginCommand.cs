using MediatR;
using System.ComponentModel.DataAnnotations;
using DevHub.Application.Common;

namespace DevHub.Application.Authentication.Commands.Login;

/// <summary>
/// Credentials used to authenticate a user.
/// </summary>
/// <param name="Email">The account email address.</param>
/// <param name="Password">The account password.</param>
public sealed record LoginCommand(
    [Required, StringLength(320)] string Email,
    [Required, StringLength(128)] string Password)
    : IRequest<Result<AuthenticationResponse>>;
