namespace DevHub.Application.Administration;

public interface IPlatformOverview
{
    Task<PlatformOverview> GetAsync(CancellationToken cancellationToken);
}

public sealed record PlatformOverview(
    int UserCount,
    int OrganizationCount,
    int BugReportCount,
    IReadOnlyList<PlatformUser> RecentUsers,
    IReadOnlyList<PlatformOrganization> RecentOrganizations,
    IReadOnlyList<PlatformBugReport> RecentBugReports);

public sealed record PlatformUser(Guid Id, string Username, DateTimeOffset CreatedAtUtc);

public sealed record PlatformOrganization(Guid Id, string Name, DateTimeOffset CreatedAtUtc);

public sealed record PlatformBugReport(
    string Reference, string Title, string Status, string Priority, DateTimeOffset CreatedAtUtc);
