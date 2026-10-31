using DevHub.Application.Common;
using DevHub.Application.Workshops;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Workshops;

internal sealed class WorkshopStorage(ApplicationDbContext db, WorkshopLimits limits) : IWorkshopStorage
{
    public async Task<WorkshopStorageUsage> Get(Guid organizationId, CancellationToken ct)
    {
        var used = await db.BugReportAttachments.Where(file => db.BugReports.Any(report => report.Id == file.BugReportId
            && db.Projects.Any(project => project.Id == report.ProjectId && project.OrganizationId == organizationId)))
            .SumAsync(file => (long?)file.Size, ct) ?? 0;
        return new(used, limits.AttachmentBytesPerWorkshop, used >= limits.AttachmentBytesPerWorkshop * .8, used >= limits.AttachmentBytesPerWorkshop);
    }

    public async Task<bool> Lock(CancellationToken ct)
    {
        // Every attachment writer holds this transaction-owned lock through commit.
        // The global budget otherwise races across different projects/workshops.
        var results = await db.Database.SqlQuery<int>($"DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource = N'devhub:attachment-capacity', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 5000; SELECT @result AS [Value];").ToListAsync(ct);
        return results.Single() >= 0;
    }

    public async Task<Error?> Check(Guid organizationId, long incoming, CancellationToken ct)
    {
        if (incoming == 0) return null;
        var usage = await Get(organizationId, ct);
        var total = await db.BugReportAttachments.SumAsync(file => (long?)file.Size, ct) ?? 0;
        return incoming > limits.AttachmentBytesPerWorkshop - usage.UsedBytes || incoming > limits.AttachmentBytesTotal - total
            ? new("Workshop.StorageCapacity", "Attachment storage is full. Your report and selected files are preserved. Ask a workshop administrator to free storage or raise its quota.", ErrorType.Conflict)
            : null;
    }
}
