using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Notifications;
using DevHub.Domain.Notifications;
using DevHub.Domain.Organizations;
using DevHub.Domain.Todos;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Notifications;

internal sealed class NotificationService(ApplicationDbContext db, IOrganizationRepository organizations, TimeProvider clock) : INotificationService
{
    private static readonly Error Missing = new("Notifications.NotFound", "The notification or organization is unavailable.", ErrorType.NotFound);
    private async Task<IQueryable<UserNotification>?> Visible(Guid organizationId, Guid userId, CancellationToken ct)
    {
        var organization = await organizations.GetByIdAsync(organizationId, ct);
        if (organization is null || !organization.HasMember(userId)) return null;
        var viewAll = organization.HasPermission(userId, OrganizationPermissions.ViewAllProjects);
        var teams = organization.Teams.Where(t => t.HasMember(userId)).Select(t => t.Id).ToArray();
        // Recheck current membership and project access when listing and marking read.
        return db.UserNotifications.Where(n => n.OrganizationId == organizationId && n.UserId == userId &&
            (n.ProjectId == null || db.Projects.Any(p => p.Id == n.ProjectId && p.OrganizationId == organizationId && (viewAll || teams.Contains(p.TeamId)))) &&
            (n.Kind != UserNotification.TaskAssigned || db.Set<Todo>().Any(t => t.Id == n.ContextId && t.ProjectId == n.ProjectId && t.Tasks.Any(task => task.Id == n.TargetId))));
    }
    public async Task<Result<NotificationPageResponse>> List(Guid organizationId, Guid userId, long? before, bool unreadOnly, CancellationToken ct)
    {
        if (before is <= 0) return Result<NotificationPageResponse>.Failure(new("Notifications.InvalidCursor", "Choose a valid notification page.", ErrorType.Validation));
        var visible = await Visible(organizationId, userId, ct);
        if (visible is null) return Result<NotificationPageResponse>.Failure(Missing);
        var unreadCount = await visible.CountAsync(n => n.ReadAtUtc == null, ct);
        var latestId = await visible.Select(n => (long?)n.Id).MaxAsync(ct) ?? 0;
        var query = visible.AsNoTracking();
        if (unreadOnly) query = query.Where(n => n.ReadAtUtc == null);
        if (before.HasValue) query = query.Where(n => n.Id < before.Value);
        var items = await query.OrderByDescending(n => n.Id).Take(26).Select(n => new UserNotificationResponse(
            n.Id, n.Kind, n.Title, db.Users.Where(u => u.Id == n.ActorUserId).Select(u => u.FirstName + " " + u.LastName).FirstOrDefault() ?? (n.ActorUserId == Guid.Empty ? "Public reporter" : "Former member"),
            n.ProjectId, db.Projects.Where(p => p.Id == n.ProjectId).Select(p => p.Name).FirstOrDefault(), n.ContextId, n.TargetId, n.CreatedAtUtc, n.ReadAtUtc)).ToListAsync(ct);
        return Result<NotificationPageResponse>.Success(new(items.Take(25).ToArray(), unreadCount, items.Count > 25 ? items[24].Id : null, latestId));
    }
    public async Task<Result<bool>> MarkRead(Guid organizationId, Guid userId, long id, CancellationToken ct)
    {
        var visible = await Visible(organizationId, userId, ct);
        var notification = visible is null ? null : await visible.SingleOrDefaultAsync(n => n.Id == id, ct);
        if (notification is null) return Result<bool>.Failure(Missing);
        notification.MarkRead(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Result<bool>.Success(true);
    }
    public async Task<Result<bool>> MarkAllRead(Guid organizationId, Guid userId, long throughId, CancellationToken ct)
    {
        if (throughId < 0) return Result<bool>.Failure(new("Notifications.InvalidCursor", "Choose a valid notification boundary.", ErrorType.Validation));
        var visible = await Visible(organizationId, userId, ct);
        if (visible is null) return Result<bool>.Failure(Missing);
        // Bound the action to notifications the client has seen; concurrent arrivals stay unread.
        await visible.Where(n => n.Id <= throughId && n.ReadAtUtc == null).ExecuteUpdateAsync(update => update.SetProperty(n => n.ReadAtUtc, clock.GetUtcNow()), ct);
        return Result<bool>.Success(true);
    }
}
