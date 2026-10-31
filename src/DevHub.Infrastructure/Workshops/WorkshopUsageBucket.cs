namespace DevHub.Infrastructure.Workshops;

internal sealed class WorkshopUsageBucket
{
    public string Key { get; set; } = "";
    public DateTimeOffset WindowStartUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public int Used { get; set; }
}
