using DevHub.Application.Common;

namespace DevHub.Application.Workshops;

public sealed class WorkshopLimits
{
    public const string Section = "Security:Workshop";
    public int ChatMessagesPerMinute { get; set; } = 30;
    public int ChatBurst { get; set; } = 5;
    public int SavesPerMinute { get; set; } = 30;
    public int CreatesPerMinute { get; set; } = 30;
    public int InvitesPerUserPerDay { get; set; } = 20;
    public int InvitesPerWorkshopPerDay { get; set; } = 50;
    public int CanvasWarningObjects { get; set; } = 1000;
    public int CanvasMaximumObjects { get; set; } = 2000;
    public int MaximumMentions { get; set; } = 10;
    public int NotificationsPerMinute { get; set; } = 30;
    public long AttachmentBytesPerWorkshop { get; set; } = 100L * 1024 * 1024;
    public long AttachmentBytesTotal { get; set; } = 1024L * 1024 * 1024;
}

public sealed record WorkshopCharge(string Key, int Limit, int Units, TimeSpan Window);

public interface IWorkshopUsage
{
    Task<Error?> Reserve(IReadOnlyList<WorkshopCharge> charges, CancellationToken ct);
    Task<int> Take(string key, int limit, int requested, TimeSpan window, CancellationToken ct);
}
