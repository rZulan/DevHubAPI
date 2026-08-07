using MediatR;
using DevHub.Application.Common;
using DevHub.Application.Users;

namespace DevHub.Application.Authentication.ExternalAuthentication;

public sealed record LinkExternalAccountCommand(Guid UserId, ExternalIdentity Identity)
    : IRequest<Result<UserResponse>>;
