using System.Data;
using System.Net.Mail;
using System.Security.Cryptography;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.BugReports;
using DevHub.Application.Common;
using DevHub.Application.Ideas;
using DevHub.Domain.Organizations;
using DevHub.Domain.Projects;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using DevHub.Application.Notifications;

namespace DevHub.Infrastructure.BugReports;

internal sealed class BugReportsService(ApplicationDbContext db, IIdeasService access, IOrganizationRepository organizations, IWorkshopActivity activity,
    DevHub.Infrastructure.Workshops.WorkshopStorage storage) : IBugReportsService
{
    private static readonly Error Missing = new("Bugs.NotFound", "The report or submission link is unavailable.", ErrorType.NotFound);
    private static readonly Error Conflict = new("Bugs.Conflict", "This item changed in another session. Reload and try again.", ErrorType.Conflict);
    private static Error Invalid(string message) => new("Bugs.Invalid", message, ErrorType.Validation);
    private static string Token() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private static bool ValidStatus(string status) => status is "new" or "triaged" or "in-progress" or "resolved" or "closed";
    private static bool ValidPriority(string priority) => priority is "low" or "medium" or "high" or "critical";
    private async Task<Error?> Access(BugScope scope, bool write, CancellationToken ct)
    {
        if (!await access.CanView(scope.OrganizationId, scope.ProjectId, scope.UserId, ct)) return Missing;
        if (write && (await organizations.GetByIdAsync(scope.OrganizationId, ct))?.HasPermission(scope.UserId, OrganizationPermissions.ManageBugReports) != true)
            return new("Bugs.Forbidden", "You need Manage bug reports permission to make this change.", ErrorType.Forbidden);
        return null;
    }
    private Task<List<BugCategoryResponse>> Categories(Guid projectId, CancellationToken ct) => db.BugCategories.AsNoTracking().Where(c => c.ProjectId == projectId).OrderBy(c => c.Name).Select(c => new BugCategoryResponse(c.Id, c.Name, c.Archived)).ToListAsync(ct);
    public async Task<Result<BugInboxResponse>> List(BugScope scope, string? status, Guid? categoryId, string? search, int page, CancellationToken ct)
    {
        if (await Access(scope, false, ct) is { } error) return Result<BugInboxResponse>.Failure(error);
        if (page < 0 || page > 10000 || status is not null && !ValidStatus(status) || search?.Length > 200) return Result<BugInboxResponse>.Failure(Invalid("Invalid report filters."));
        var all = db.BugReports.AsNoTracking().Where(r => r.ProjectId == scope.ProjectId);
        var query = all;
        if (status is not null) query = query.Where(r => r.Status == status);
        if (categoryId is not null) query = query.Where(r => r.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search)) {var term = search.Trim(); query = query.Where(r => r.Title.Contains(term) || r.Email.Contains(term) || r.Reference.Contains(term));}
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id).Skip(page * 25).Take(25)
            .Select(r => new BugSummary(r.Id, r.Reference, r.Email, r.Title, r.Status, r.Priority, r.CategoryId, r.Source, r.CreatedAtUtc, r.Attachments.Count)).ToListAsync(ct);
        return Result<BugInboxResponse>.Success(new(items, total, await all.CountAsync(r => r.Status == "new", ct), await all.CountAsync(r => r.Status != "resolved" && r.Status != "closed", ct), await all.CountAsync(r => r.Status == "resolved" || r.Status == "closed", ct), await Categories(scope.ProjectId, ct)));
    }
    private IQueryable<BugDetail> Details(Guid projectId, Guid reportId) => db.BugReports.AsNoTracking().Where(r => r.ProjectId == projectId && r.Id == reportId)
        .Select(r => new BugDetail(r.Id, r.Reference, r.Email, r.Title, r.Description, r.Steps, r.Environment, r.Status, r.Priority, r.CategoryId, r.InternalNotes, r.Source, r.CreatedAtUtc, r.UpdatedAtUtc, r.Revision, r.Attachments.Select(a => new BugAttachmentResponse(a.Id, a.FileName, a.ContentType, a.Size)).ToList()));
    public async Task<Result<BugDetail>> Get(BugScope scope, Guid reportId, CancellationToken ct)
    {
        if (await Access(scope, false, ct) is { } error) return Result<BugDetail>.Failure(error);
        var report = await Details(scope.ProjectId, reportId).SingleOrDefaultAsync(ct);
        return report is null ? Result<BugDetail>.Failure(Missing) : Result<BugDetail>.Success(report);
    }
    public async Task<Result<BugDetail>> Triage(BugScope scope, Guid reportId, TriageBugRequest request, CancellationToken ct)
    {
        if (await Access(scope, true, ct) is { } error) return Result<BugDetail>.Failure(error);
        if (!ValidStatus(request.Status) || !ValidPriority(request.Priority) || request.InternalNotes is null || request.InternalNotes.Length > 10000 || request.Revision < 0 || request.Revision == int.MaxValue) return Result<BugDetail>.Failure(Invalid("Invalid status, priority, or internal notes."));
        var report = await db.BugReports.SingleOrDefaultAsync(r => r.ProjectId == scope.ProjectId && r.Id == reportId, ct);
        if (report is null) return Result<BugDetail>.Failure(Missing);
        if (report.Revision != request.Revision) return Result<BugDetail>.Failure(Conflict);
        if (request.CategoryId is { } category && !await db.BugCategories.AnyAsync(c => c.ProjectId == scope.ProjectId && c.Id == category && (!c.Archived || report.CategoryId == category), ct)) return Result<BugDetail>.Failure(Invalid("Choose an active category from this project."));
        var changed = report.Status != request.Status || report.Priority != request.Priority || report.CategoryId != request.CategoryId || report.InternalNotes != request.InternalNotes.Trim();
        report.Triage(request.Status, request.Priority, request.CategoryId, request.InternalNotes, DateTimeOffset.UtcNow);
        if (changed) await activity.Queue(new(scope.OrganizationId, scope.UserId, "bug-updated", report.Title, report.Id, report.Id, scope.ProjectId), ct);
        try {await db.SaveChangesAsync(ct);} catch (PersistenceConcurrencyException) {return Result<BugDetail>.Failure(Conflict);}
        return await Get(scope, reportId, ct);
    }
    public async Task<Result<BugAttachmentDownload>> Download(BugScope scope, Guid reportId, Guid attachmentId, CancellationToken ct)
    {
        if (await Access(scope, false, ct) is { } error) return Result<BugAttachmentDownload>.Failure(error);
        if (!await db.BugReports.AnyAsync(r => r.ProjectId == scope.ProjectId && r.Id == reportId, ct)) return Result<BugAttachmentDownload>.Failure(Missing);
        var attachment = await db.BugReportAttachments.AsNoTracking().Where(a => a.Id == attachmentId && a.BugReportId == reportId).Select(a => new BugAttachmentDownload(a.FileName, a.ContentType, a.Content)).SingleOrDefaultAsync(ct);
        return attachment is null ? Result<BugAttachmentDownload>.Failure(Missing) : Result<BugAttachmentDownload>.Success(attachment);
    }
    public async Task<Result<BugPortalResponse>> GetPortal(BugScope scope, CancellationToken ct)
    {
        if (await Access(scope, true, ct) is { } error) return Result<BugPortalResponse>.Failure(error);
        var portal = await db.BugReportPortals.AsNoTracking().SingleOrDefaultAsync(p => p.ProjectId == scope.ProjectId, ct);
        return Result<BugPortalResponse>.Success(portal is null ? new("", false, -1) : new(portal.Token, portal.Enabled, portal.Revision, BugPortalLink.ShortCode(portal.Token)));
    }
    public async Task<Result<BugPortalResponse>> SavePortal(BugScope scope, bool enabled, bool rotate, int revision, CancellationToken ct)
    {
        if (await Access(scope, true, ct) is { } error) return Result<BugPortalResponse>.Failure(error);
        var portal = await db.BugReportPortals.SingleOrDefaultAsync(p => p.ProjectId == scope.ProjectId, ct);
        if (revision < -1 || revision == int.MaxValue || revision != (portal?.Revision ?? -1)) return Result<BugPortalResponse>.Failure(Conflict);
        var changed = portal is null || portal.Enabled != enabled || rotate;
        if (portal is null) {portal = BugReportPortal.Create(scope.ProjectId, Token()); if (!enabled) portal.Update(false); db.BugReportPortals.Add(portal);}
        else portal.Update(enabled, rotate ? Token() : null);
        if (changed) await activity.Queue(new(scope.OrganizationId, scope.UserId, "bug-settings", "Bug report portal", scope.ProjectId, scope.ProjectId, scope.ProjectId), ct);
        try {await db.SaveChangesAsync(ct);} catch (PersistenceConcurrencyException) {return Result<BugPortalResponse>.Failure(Conflict);} catch (UniqueConstraintViolationException) {return Result<BugPortalResponse>.Failure(Conflict);}
        return Result<BugPortalResponse>.Success(new(portal.Token, portal.Enabled, portal.Revision, BugPortalLink.ShortCode(portal.Token)));
    }
    public async Task<Result<BugCategoryResponse>> SaveCategory(BugScope scope, Guid? id, string name, bool archived, CancellationToken ct)
    {
        if (await Access(scope, true, ct) is { } error) return Result<BugCategoryResponse>.Failure(error);
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 50) return Result<BugCategoryResponse>.Failure(Invalid("Category names must be 1–50 characters."));
        var category = id is null ? null : await db.BugCategories.SingleOrDefaultAsync(c => c.ProjectId == scope.ProjectId && c.Id == id, ct);
        if (id is not null && category is null) return Result<BugCategoryResponse>.Failure(Missing);
        if (id is null && await db.BugCategories.CountAsync(c => c.ProjectId == scope.ProjectId, ct) >= 100) return Result<BugCategoryResponse>.Failure(Invalid("This project has reached its 100-category limit. Rename or restore an existing category."));
        var normalized = name.Trim().ToUpperInvariant();
        if (await db.BugCategories.AnyAsync(c => c.ProjectId == scope.ProjectId && c.Id != id && c.NormalizedName == normalized, ct)) return Result<BugCategoryResponse>.Failure(Invalid("A category with this name already exists, including archived categories."));
        var changed = category is null || category.Name != name.Trim() || category.Archived != archived;
        if (category is null) {category = BugCategory.Create(scope.ProjectId, name); db.BugCategories.Add(category);}
        category.Update(name, archived);
        if (changed) await activity.Queue(new(scope.OrganizationId, scope.UserId, "bug-settings", "Bug report categories", scope.ProjectId, scope.ProjectId, scope.ProjectId), ct);
        try {await db.SaveChangesAsync(ct);} catch (UniqueConstraintViolationException) {return Result<BugCategoryResponse>.Failure(Invalid("A category with this name already exists."));}
        return Result<BugCategoryResponse>.Success(new(category.Id, category.Name, category.Archived));
    }
    public async Task<Result<PublicBugFormResponse>> GetPublicForm(string token, CancellationToken ct)
    {
        var lookupToken = BugPortalLink.LookupToken(token);
        if (lookupToken is null) return Result<PublicBugFormResponse>.Failure(Missing);
        var portal = await FindPublicPortal(lookupToken, ct);
        if (portal is null) return Result<PublicBugFormResponse>.Failure(Missing);
        var name = await db.Projects.Where(p => p.Id == portal.ProjectId).Select(p => p.Name).SingleAsync(ct);
        return Result<PublicBugFormResponse>.Success(new(name, (await Categories(portal.ProjectId, ct)).Where(c => !c.Archived).ToList()));
    }
    private async Task<BugReportPortal?> FindPublicPortal(string lookupToken, CancellationToken ct)
    {
        // Fail closed if a prefix ever matches more than one portal, including disabled portals.
        var matches = await db.BugReportPortals.AsNoTracking().Where(portal => portal.Token.StartsWith(lookupToken)).Take(2).ToListAsync(ct);
        return matches.Count == 1 && matches[0].Enabled ? matches[0] : null;
    }
    public Task<Result<BugReceipt>> SubmitPublic(string token, SubmitBugRequest request, CancellationToken ct) => Submit(null, token, request, ct);
    public Task<Result<BugReceipt>> SubmitTeam(BugScope scope, SubmitBugRequest request, CancellationToken ct) => Submit(scope, null, request, ct);
    private async Task<Result<BugReceipt>> Submit(BugScope? scope, string? token, SubmitBugRequest request, CancellationToken ct)
    {
        if (scope is not null && await Access(scope, true, ct) is { } accessError) return Result<BugReceipt>.Failure(accessError);
        var lookupToken = token is null ? null : BugPortalLink.LookupToken(token);
        if (scope is null && lookupToken is null) return Result<BugReceipt>.Failure(Missing);
        if (request.SubmissionId == Guid.Empty || string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 || !MailAddress.TryCreate(request.Email.Trim(), out var mail) || mail.Address != request.Email.Trim()
            || string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 200 || string.IsNullOrWhiteSpace(request.Description) || request.Description.Length > 20000
            || request.Steps is null || request.Steps.Length > 10000 || request.Environment is null || request.Environment.Length > 1000 || !ValidPriority(request.Priority)) return Result<BugReceipt>.Failure(Invalid("Enter a valid email, title, description, and priority within the supported lengths."));
        if (request.Attachments.Count > BugFileValidation.MaxFiles || request.Attachments.Sum(f => (long)f.Content.Length) > BugFileValidation.MaxTotalBytes) return Result<BugReceipt>.Failure(Invalid("Attach up to 5 files, 10 MB each and 25 MB total."));
        var files = request.Attachments.Select(f => (Name: BugFileValidation.SafeName(f.FileName), f.Content)).ToList();
        if (files.Any(f => BugFileValidation.ContentType(f.Name, f.Content) is null)) return Result<BugReceipt>.Failure(Invalid("An attachment is empty, too large, or its content does not match a supported file type. Use PNG, JPG, GIF, WebP, PDF, ZIP, MP4, WebM, or UTF-8 text, log, CSV, JSON files."));
        // The transaction also rechecks the live link, so disabling it prevents new submissions.
        var attempts = 0;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            if (attempts++ > 0) db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (files.Count > 0 && !await storage.Lock(ct))
                return Result<BugReceipt>.Failure(new("Workshop.StorageBusy", "Attachment storage is busy. Your selected files are preserved; please retry shortly.", ErrorType.Throttled, RetryAfterSeconds: 5));
            var portal = scope is null ? await FindPublicPortal(lookupToken!, ct) : null;
            if (scope is null && portal is null) return Result<BugReceipt>.Failure(Missing);
            var projectId = scope?.ProjectId ?? portal!.ProjectId;
            var previous = await db.BugReports.AsNoTracking().Where(r => r.ProjectId == projectId && r.SubmissionId == request.SubmissionId).Select(r => new {r.Reference, r.Email}).SingleOrDefaultAsync(ct);
            if (previous is not null) return previous.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase) ? Result<BugReceipt>.Success(new(previous.Reference)) : Result<BugReceipt>.Failure(Conflict);
            if (request.CategoryId is { } categoryId && !await db.BugCategories.AnyAsync(c => c.ProjectId == projectId && c.Id == categoryId && !c.Archived, ct)) return Result<BugReceipt>.Failure(Invalid("This category is no longer available. Refresh the form and choose an active category."));
            if (scope is null)
            {
                var since = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
                var daily = db.BugReports.Where(r => r.ProjectId == projectId && r.CreatedAtUtc >= since && r.Source == "public");
                var bytes = await daily.SelectMany(r => r.Attachments).SumAsync(a => (long?)a.Size, ct) ?? 0;
                if (await daily.CountAsync(ct) >= 200 || bytes + files.Sum(f => (long)f.Content.Length) > 256L * 1024 * 1024) return Result<BugReceipt>.Failure(new("Bugs.Capacity", "This project's public inbox is temporarily full. Please try again later.", ErrorType.Conflict));
            }
            var organizationId = scope?.OrganizationId ?? await db.Projects.Where(p => p.Id == projectId).Select(p => p.OrganizationId).SingleAsync(ct);
            if (await storage.Check(organizationId, files.Sum(file => (long)file.Content.Length), ct) is { } storageError)
                return Result<BugReceipt>.Failure(storageError);
            var report = BugReport.Create(projectId, request.SubmissionId, request.Email, request.Title, request.Description, request.Steps, request.Environment, request.CategoryId, request.Priority, scope is null ? "public" : "team", DateTimeOffset.UtcNow);
            foreach (var file in files) report.Attachments.Add(BugReportAttachment.Create(report.Id, file.Name, BugFileValidation.ContentType(file.Name, file.Content)!, file.Content));
            db.BugReports.Add(report);
            await activity.Queue(new(organizationId, scope?.UserId ?? Guid.Empty, "bug-created", report.Title, report.Id, report.Id, projectId), ct);
            await db.SaveChangesAsync(ct); await tx.CommitAsync(ct);
            return Result<BugReceipt>.Success(new(report.Reference));
        });
    }
}
