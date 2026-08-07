using MediatR;
using DevHub.Application.Common;

namespace DevHub.Application.Authentication.Commands.Logout;

public sealed record LogoutCommand(Guid UserId, string? RefreshToken)
    : IRequest<Result<bool>>;
