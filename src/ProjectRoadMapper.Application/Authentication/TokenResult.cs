namespace ProjectRoadMapper.Application.Authentication;

public sealed record TokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc);
