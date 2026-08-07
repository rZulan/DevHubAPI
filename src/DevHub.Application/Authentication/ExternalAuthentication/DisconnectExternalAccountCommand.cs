using MediatR;
using DevHub.Application.Common;

namespace DevHub.Application.Authentication.ExternalAuthentication;

public sealed record DisconnectExternalAccountCommand(Guid UserId, string Provider)
    : IRequest<Result<bool>>;
