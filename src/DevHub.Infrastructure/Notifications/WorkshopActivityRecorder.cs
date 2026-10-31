using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Notifications;
using DevHub.Domain.Notifications;
using DevHub.Domain.Organizations;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Notifications;

internal sealed class WorkshopActivityRecorder(ApplicationDbContext db, IOrganizationRepository organizations, TimeProvider clock) : IWorkshopActivity
{
    public async Task Queue(WorkshopActivity activity, CancellationToken ct)
    {
        var organization = await organizations.GetByIdAsync(activity.OrganizationId, ct);
        if (organization is null) return;
        var project = activity.ProjectId is { } projectId
            ? db.Projects.Local.FirstOrDefault(p => p.Id == projectId) ?? await db.Projects.SingleOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == activity.OrganizationId, ct)
            : null;
        if (activity.ProjectId is not null && project is null) return;
        var recipients = organization.Members.Where(m => m.UserId != activity.ActorUserId &&
            (project is null || organization.HasPermission(m.UserId, OrganizationPermissions.ViewAllProjects) || organization.Teams.Any(t => t.Id == project.TeamId && t.HasMember(m.UserId))))
            .Select(m => m.UserId).ToArray();
        var now = clock.GetUtcNow();
        var recent = await db.UserNotifications.Where(n => n.OrganizationId == activity.OrganizationId && n.Kind == activity.Kind &&
            n.TargetId == activity.TargetId && n.ActorUserId == activity.ActorUserId && n.ReadAtUtc == null && n.CreatedAtUtc >= now.AddMinutes(-5))
            .Select(n => n.UserId).ToListAsync(ct);
        foreach (var userId in recipients.Except(recent))
        {
            // An assignment notification already explains this action to its new assignee.
            if (db.UserNotifications.Local.Any(n => n.UserId == userId && n.TargetId == activity.TargetId && db.Entry(n).State == EntityState.Added)) continue;
            db.UserNotifications.Add(new UserNotification(activity.OrganizationId, userId, activity.ActorUserId, activity.Kind, activity.Title,
                activity.ContextId, activity.TargetId, now, activity.ProjectDeleted ? null : activity.ProjectId));
        }
    }
}
