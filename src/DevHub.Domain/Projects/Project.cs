using DevHub.Domain.Common;
using DevHub.Domain.Organizations;
using DevHub.Domain.Teams;
using DevHub.Domain.Users;

namespace DevHub.Domain.Projects;

public enum ProjectStatus
{
    Planning,
    InDevelopment,
    Review,
    OnHold,
    Shipped,
    Archived
}

public sealed class Project : BaseEntity
{
    private Project()
    {
    }

    private Project(
        Guid id,
        Guid organizationId,
        Guid teamId,
        Guid leadUserId,
        string name,
        string summary,
        ProjectStatus status,
        IEnumerable<string> techStack,
        string? repositoryUrl,
        string? documentationUrl,
        string? designUrl,
        string? liveUrl,
        string? sdlcMethod,
        string? databaseDetails,
        IEnumerable<string> environments,
        DateOnly? startDate,
        DateOnly? targetDate,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        OrganizationId = organizationId;
        SetDetails(
            teamId,
            leadUserId,
            name,
            summary,
            status,
            techStack,
            repositoryUrl,
            documentationUrl,
            designUrl,
            liveUrl,
            sdlcMethod,
            databaseDetails,
            environments,
            startDate,
            targetDate);
    }

    public Guid OrganizationId { get; private set; }
    public string IdeasJson { get; private set; } = "[]";
    public int IdeasRevision { get; private set; }
    public Guid TeamId { get; private set; }
    public Guid LeadUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string NormalizedName { get; private set; } = string.Empty;
    public string Summary { get; private set; } = string.Empty;
    public ProjectStatus Status { get; private set; }
    public string TechStack { get; private set; } = string.Empty;
    public string? RepositoryUrl { get; private set; }
    public string? DocumentationUrl { get; private set; }
    public string? DesignUrl { get; private set; }
    public string? LiveUrl { get; private set; }
    public string? SdlcMethod { get; private set; }
    public string? DatabaseDetails { get; private set; }
    public string Environments { get; private set; } = string.Empty;
    public DateOnly? StartDate { get; private set; }
    public DateOnly? TargetDate { get; private set; }
    public Organization Organization { get; private set; } = null!;
    public Team Team { get; private set; } = null!;
    public User Lead { get; private set; } = null!;

    public static Project Create(
        Guid organizationId,
        Guid teamId,
        Guid leadUserId,
        string name,
        string summary,
        ProjectStatus status,
        IEnumerable<string> techStack,
        string? repositoryUrl,
        string? documentationUrl,
        string? designUrl,
        string? liveUrl,
        string? sdlcMethod,
        string? databaseDetails,
        IEnumerable<string> environments,
        DateOnly? startDate,
        DateOnly? targetDate,
        DateTimeOffset createdAtUtc) =>
        new(
            Guid.NewGuid(),
            organizationId,
            teamId,
            leadUserId,
            name,
            summary,
            status,
            techStack,
            repositoryUrl,
            documentationUrl,
            designUrl,
            liveUrl,
            sdlcMethod,
            databaseDetails,
            environments,
            startDate,
            targetDate,
            createdAtUtc);

    public void Update(
        Guid teamId,
        Guid leadUserId,
        string name,
        string summary,
        ProjectStatus status,
        IEnumerable<string> techStack,
        string? repositoryUrl,
        string? documentationUrl,
        string? designUrl,
        string? liveUrl,
        string? sdlcMethod,
        string? databaseDetails,
        IEnumerable<string> environments,
        DateOnly? startDate,
        DateOnly? targetDate,
        DateTimeOffset updatedAtUtc)
    {
        SetDetails(
            teamId,
            leadUserId,
            name,
            summary,
            status,
            techStack,
            repositoryUrl,
            documentationUrl,
            designUrl,
            liveUrl,
            sdlcMethod,
            databaseDetails,
            environments,
            startDate,
            targetDate);
        MarkUpdated(updatedAtUtc);
    }

    public static string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim().ToUpperInvariant();
    }

    public static string JoinValues(IEnumerable<string> values) => string.Join(
        ", ",
        values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase));

    public static IReadOnlyList<string> SplitValues(string values) => values
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToArray();

    private void SetDetails(
        Guid teamId,
        Guid leadUserId,
        string name,
        string summary,
        ProjectStatus status,
        IEnumerable<string> techStack,
        string? repositoryUrl,
        string? documentationUrl,
        string? designUrl,
        string? liveUrl,
        string? sdlcMethod,
        string? databaseDetails,
        IEnumerable<string> environments,
        DateOnly? startDate,
        DateOnly? targetDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);

        TeamId = teamId;
        LeadUserId = leadUserId;
        Name = name.Trim();
        NormalizedName = NormalizeName(name);
        Summary = summary.Trim();
        Status = status;
        TechStack = JoinValues(techStack);
        RepositoryUrl = Clean(repositoryUrl);
        DocumentationUrl = Clean(documentationUrl);
        DesignUrl = Clean(designUrl);
        LiveUrl = Clean(liveUrl);
        SdlcMethod = Clean(sdlcMethod);
        DatabaseDetails = Clean(databaseDetails);
        Environments = JoinValues(environments);
        StartDate = startDate;
        TargetDate = targetDate;
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
