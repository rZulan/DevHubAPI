using MediatR;
using ProjectRoadMapper.Application.Common;

namespace ProjectRoadMapper.Application.Authentication.Commands.RefreshAccessToken;

/// <summary>
/// A request to exchange a refresh token for a new token pair.
/// </summary>
/// <param name="RefreshToken">The opaque refresh token returned by a previous authentication response.</param>
public sealed record RefreshAccessTokenCommand(string RefreshToken)
    : IRequest<Result<AuthenticationResponse>>;
