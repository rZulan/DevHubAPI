using MediatR;
using DevHub.Application.Common;

namespace DevHub.Application.Authentication.ExternalAuthentication;

public sealed record ExternalSignInCommand(ExternalIdentity Identity)
    : IRequest<Result<AuthenticationResponse>>;
