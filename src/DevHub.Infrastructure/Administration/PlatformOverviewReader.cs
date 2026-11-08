using DevHub.Application.Administration;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Administration;

internal sealed class PlatformOverviewReader(ApplicationDbContext db) : IPlatformOverview
{
    private const int RecentLimit = 12;

    public async Task<PlatformOverview> GetAsync(CancellationToken cancellationToken)
    {
        var users = await db.Users
            .OrderByDescending(user => user.CreatedAtUtc).ThenBy(user => user.Id).Take(RecentLimit)
            .Select(user => new PlatformUser(user.Id, user.Username, user.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        var organizations = await db.Organizations
            .OrderByDescending(organization => organization.CreatedAtUtc).ThenBy(organization => organization.Id).Take(RecentLimit)
            .Select(organization => new PlatformOrganization(organization.Id, organization.Name, organization.CreatedAtUtc))
            .ToListAsync(cancellationToken);
        var reports = await db.BugReports
            .OrderByDescending(report => report.CreatedAtUtc).ThenBy(report => report.Id).Take(RecentLimit)
            .Select(report => new PlatformBugReport(report.Reference, report.Title, report.Status, report.Priority, report.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return new PlatformOverview(
            await db.Users.CountAsync(cancellationToken),
            await db.Organizations.CountAsync(cancellationToken),
            await db.BugReports.CountAsync(cancellationToken),
            users, organizations, reports);
    }
}
