namespace DevHub.Domain.Notifications;

public sealed class UserNotification
{
    public const string TaskAssigned = "task-assigned";
    private UserNotification() { }
    public UserNotification(Guid organizationId, Guid userId, Guid actorUserId, string kind, string title,
        Guid contextId, Guid targetId, DateTimeOffset now, Guid? projectId = null)
    {
        OrganizationId = organizationId; UserId = userId; ActorUserId = actorUserId;
        Kind = kind; Title = title; ContextId = contextId; TargetId = targetId;
        CreatedAtUtc = now; ProjectId = projectId;
    }
    public long Id { get; private set; }
    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ActorUserId { get; private set; }
    public string Kind { get; private set; } = "";
    public string Title { get; private set; } = "";
    public Guid ContextId { get; private set; }
    public Guid TargetId { get; private set; }
    public Guid? ProjectId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ReadAtUtc { get; private set; }
    public void MarkRead(DateTimeOffset now) => ReadAtUtc ??= now;
}
