using DevHub.Domain.Common;

namespace DevHub.Domain.Projects;

public sealed class BugReport : BaseEntity
{
    private BugReport() { }
    public Guid ProjectId { get; private set; }
    public Guid SubmissionId { get; private set; }
    public string Reference { get; private set; } = "";
    public string Email { get; private set; } = "";
    public string Title { get; private set; } = "";
    public string Description { get; private set; } = "";
    public string Steps { get; private set; } = "";
    public string Environment { get; private set; } = "";
    public string Source { get; private set; } = "public";
    public string Status { get; private set; } = "new";
    public string Priority { get; private set; } = "medium";
    public Guid? CategoryId { get; private set; }
    public string InternalNotes { get; private set; } = "";
    public int Revision { get; private set; }
    public List<BugReportAttachment> Attachments { get; private set; } = [];

    public static BugReport Create(Guid projectId, Guid submissionId, string email, string title, string description, string steps, string environment, Guid? categoryId, string priority, string source, DateTimeOffset now)
    {
        return new BugReport(Guid.NewGuid(), now) {ProjectId = projectId, SubmissionId = submissionId, Email = email.Trim(), Title = title.Trim(), Description = description.Trim(), Steps = steps.Trim(), Environment = environment.Trim(), CategoryId = categoryId, Priority = priority, Source = source};
    }
    private BugReport(Guid id, DateTimeOffset now) : base(id, now)
    {
        Reference = $"BUG-{id.ToString("N")[..12].ToUpperInvariant()}";
    }
    public void Triage(string status, string priority, Guid? categoryId, string internalNotes, DateTimeOffset now)
    {
        Status = status; Priority = priority; CategoryId = categoryId; InternalNotes = internalNotes.Trim(); Revision++; MarkUpdated(now);
    }
}

public sealed class BugReportAttachment
{
    private BugReportAttachment() { }
    public Guid Id { get; private set; }
    public Guid BugReportId { get; private set; }
    public string FileName { get; private set; } = "";
    public string ContentType { get; private set; } = "application/octet-stream";
    public int Size { get; private set; }
    public byte[] Content { get; private set; } = [];
    public static BugReportAttachment Create(Guid reportId, string fileName, string contentType, byte[] content) => new() {Id = Guid.NewGuid(), BugReportId = reportId, FileName = fileName, ContentType = contentType, Size = content.Length, Content = content};
}

public sealed class BugCategory
{
    private BugCategory() { }
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = "";
    public string NormalizedName { get; private set; } = "";
    public bool Archived { get; private set; }
    public static BugCategory Create(Guid projectId, string name) {var category = new BugCategory {Id = Guid.NewGuid(), ProjectId = projectId}; category.Update(name, false); return category;}
    public void Update(string name, bool archived) {Name = name.Trim(); NormalizedName = Name.ToUpperInvariant(); Archived = archived;}
}

public sealed class BugReportPortal
{
    private BugReportPortal() { }
    public Guid ProjectId { get; private set; }
    public string Token { get; private set; } = "";
    public bool Enabled { get; private set; }
    public int Revision { get; private set; }
    public static BugReportPortal Create(Guid projectId, string token) => new() {ProjectId = projectId, Token = token, Enabled = true};
    public void Update(bool enabled, string? token = null) {Enabled = enabled; if (token is not null) Token = token; Revision++;}
}
