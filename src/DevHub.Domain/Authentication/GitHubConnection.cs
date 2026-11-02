namespace DevHub.Domain.Authentication;

public sealed class GitHubConnection
{
    public Guid UserId { get; set; }
    public long GitHubUserId { get; set; }
    public string Login { get; set; } = "";
    public string ProtectedAccessToken { get; set; } = "";
    public string? ProtectedRefreshToken { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? RefreshExpiresAtUtc { get; set; }
    public byte[] Version { get; set; } = [];
}
