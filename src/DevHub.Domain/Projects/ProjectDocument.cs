namespace DevHub.Domain.Projects;

/// <summary>The current saved editor document. Revisions protect whole-document saves.</summary>
public sealed class ProjectDocument
{
    public Guid Id { get; private set; }
    public Guid ProjectId { get; private set; }
    public string ToolType { get; private set; } = string.Empty;
    public string? ContentJson { get; private set; }
    public int Revision { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    // Historical documents did not record their editor or document-specific save time.
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
}
