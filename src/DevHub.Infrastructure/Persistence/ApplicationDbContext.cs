using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Domain.Authentication;
using DevHub.Domain.Users;
using DevHub.Domain.Organizations;
using DevHub.Domain.Teams;
using DevHub.Domain.Projects;
using DevHub.Domain.Chats;

namespace DevHub.Infrastructure.Persistence;

public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options,
    Microsoft.Extensions.Options.IOptions<DevHub.Application.Workshops.WorkshopLimits>? workshopOptions = null,
    TimeProvider? timeProvider = null)
    : DbContext(options), IUnitOfWork
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<ExternalAccount> ExternalAccounts => Set<ExternalAccount>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<OrganizationInvite> OrganizationInvites => Set<OrganizationInvite>();

    public DbSet<OrganizationRole> OrganizationRoles => Set<OrganizationRole>();

    public DbSet<OrganizationMemberRole> OrganizationMemberRoles => Set<OrganizationMemberRole>();

    public DbSet<OrganizationColorScheme> OrganizationColorSchemes => Set<OrganizationColorScheme>();

    public DbSet<Team> Teams => Set<Team>();

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<ProjectDocument> ProjectDocuments => Set<ProjectDocument>();

    public DbSet<BugReport> BugReports => Set<BugReport>();
    public DbSet<BugCategory> BugCategories => Set<BugCategory>();
    public DbSet<BugReportPortal> BugReportPortals => Set<BugReportPortal>();
    public DbSet<BugReportAttachment> BugReportAttachments => Set<BugReportAttachment>();

    public DbSet<ChatConversation> ChatConversations => Set<ChatConversation>();

    public DbSet<ChatParticipant> ChatParticipants => Set<ChatParticipant>();

    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<DevHub.Domain.Notifications.UserNotification> UserNotifications => Set<DevHub.Domain.Notifications.UserNotification>();

    public override async Task<int> SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Apply the fan-out budget to every notification producer, including assignments.
            // Suppressing excess activity notices must not discard the underlying user action.
            var notifications = ChangeTracker.Entries<DevHub.Domain.Notifications.UserNotification>()
                .Where(entry => entry.State == EntityState.Added).ToArray();
            if (notifications.Length > 0 && Database.IsSqlServer())
            {
                var usage = new DevHub.Infrastructure.Workshops.WorkshopUsage(options, timeProvider ?? TimeProvider.System);
                var limits = workshopOptions?.Value ?? new();
                foreach (var group in notifications.GroupBy(entry => entry.Entity.ActorUserId == Guid.Empty
                    ? $"notify-public:{entry.Entity.OrganizationId:N}" : $"notify-user:{entry.Entity.ActorUserId:N}"))
                {
                    var ordered = group.OrderByDescending(entry => entry.Entity.Kind == DevHub.Domain.Notifications.UserNotification.TaskAssigned).ToArray();
                    var granted = await usage.Take(group.Key, limits.NotificationsPerMinute, ordered.Length, TimeSpan.FromMinutes(1), cancellationToken);
                    foreach (var excess in ordered.Skip(granted)) excess.State = EntityState.Detached;
                }
            }
            return await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PersistenceConcurrencyException(
                "A concurrent persistence operation modified the same data.",
                exception);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new UniqueConstraintViolationException(
                "A unique database constraint was violated.",
                exception);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
