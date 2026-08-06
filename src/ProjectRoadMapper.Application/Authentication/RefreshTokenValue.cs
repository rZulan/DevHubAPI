namespace ProjectRoadMapper.Application.Authentication;

public sealed record RefreshTokenValue(
    string Token,
    string TokenHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);
