using DevHub.Application.Common;

namespace DevHub.Application.Notifications;

public sealed record UserNotificationResponse(long Id, string Kind, string Title, string ActorName,
    Guid? ProjectId, string? ProjectName, Guid ContextId, Guid TargetId, DateTimeOffset CreatedAtUtc, DateTimeOffset? ReadAtUtc);
public sealed record NotificationPageResponse(IReadOnlyList<UserNotificationResponse> Items, int UnreadCount, long? NextCursor, long LatestId);
public sealed record MarkNotificationsReadRequest(long ThroughId);
public sealed record WorkshopActivity(Guid OrganizationId, Guid ActorUserId, string Kind, string Title, Guid ContextId,
    Guid TargetId, Guid? ProjectId = null, bool ProjectDeleted = false);
public interface IWorkshopActivity
{
    // Queue activity in the caller's unit of work; a failed mutation must not produce notifications.
    Task Queue(WorkshopActivity activity, CancellationToken ct);
}
public interface INotificationService
{
    Task<Result<NotificationPageResponse>> List(Guid organizationId, Guid userId, long? before, bool unreadOnly, CancellationToken ct);
    Task<Result<bool>> MarkRead(Guid organizationId, Guid userId, long id, CancellationToken ct);
    Task<Result<bool>> MarkAllRead(Guid organizationId, Guid userId, long throughId, CancellationToken ct);
}
