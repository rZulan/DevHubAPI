using DevHub.Application.Common;

namespace DevHub.Application.BugReports;

public sealed record BugScope(Guid OrganizationId, Guid ProjectId, Guid UserId);
public sealed record BugCategoryResponse(Guid Id, string Name, bool Archived);
public sealed record BugPortalResponse(string Token, bool Enabled, int Revision, string? ShortCode = null);
public sealed record PublicBugFormResponse(string ProjectName, IReadOnlyList<BugCategoryResponse> Categories);
public sealed record BugAttachmentInput(string FileName, byte[] Content);
public sealed record SubmitBugRequest(Guid SubmissionId, string Email, string Title, string Description, string Steps, string Environment, Guid? CategoryId, string Priority, IReadOnlyList<BugAttachmentInput> Attachments);
public sealed record BugReceipt(string Reference);
public sealed record BugAttachmentResponse(Guid Id, string FileName, string ContentType, int Size);
public sealed record BugSummary(Guid Id, string Reference, string Email, string Title, string Status, string Priority, Guid? CategoryId, string Source, DateTimeOffset CreatedAtUtc, int AttachmentCount);
public sealed record BugInboxResponse(IReadOnlyList<BugSummary> Items, int Total, int NewCount, int OpenCount, int ResolvedCount, IReadOnlyList<BugCategoryResponse> Categories);
public sealed record BugDetail(Guid Id, string Reference, string Email, string Title, string Description, string Steps, string Environment, string Status, string Priority, Guid? CategoryId, string InternalNotes, string Source, DateTimeOffset CreatedAtUtc, DateTimeOffset? UpdatedAtUtc, int Revision, IReadOnlyList<BugAttachmentResponse> Attachments);
public sealed record TriageBugRequest(int Revision, string Status, string Priority, Guid? CategoryId, string InternalNotes);
public sealed record BugAttachmentDownload(string FileName, string ContentType, byte[] Content);
public interface IBugReportsService
{
    Task<Result<BugInboxResponse>> List(BugScope scope, string? status, Guid? categoryId, string? search, int page, CancellationToken ct);
    Task<Result<BugDetail>> Get(BugScope scope, Guid reportId, CancellationToken ct);
    Task<Result<BugDetail>> Triage(BugScope scope, Guid reportId, TriageBugRequest request, CancellationToken ct);
    Task<Result<BugAttachmentDownload>> Download(BugScope scope, Guid reportId, Guid attachmentId, CancellationToken ct);
    Task<Result<BugPortalResponse>> GetPortal(BugScope scope, CancellationToken ct);
    Task<Result<BugPortalResponse>> SavePortal(BugScope scope, bool enabled, bool rotate, int revision, CancellationToken ct);
    Task<Result<BugCategoryResponse>> SaveCategory(BugScope scope, Guid? id, string name, bool archived, CancellationToken ct);
    Task<Result<PublicBugFormResponse>> GetPublicForm(string token, CancellationToken ct);
    Task<Result<BugReceipt>> SubmitPublic(string token, SubmitBugRequest request, CancellationToken ct);
    Task<Result<BugReceipt>> SubmitTeam(BugScope scope, SubmitBugRequest request, CancellationToken ct);
}
