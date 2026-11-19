using System.Text.Json;
using DevHub.Application.Connections;
using DevHub.Domain.Projects;
using DevHub.Infrastructure.Connections;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.ProjectTools;

internal sealed class ProjectDocumentStore(ApplicationDbContext db, IConnectionEvents events, IConnectionCalendarSchedule calendarSchedule)
{
    public Task<ProjectDocument?> Get(Guid organizationId, Guid projectId, string tool, CancellationToken ct) =>
        db.ProjectDocuments.AsNoTracking().SingleOrDefaultAsync(document =>
            document.ProjectId == projectId && document.ToolType == tool &&
            db.Projects.Any(project => project.Id == document.ProjectId && project.OrganizationId == organizationId), ct);

    public async Task<bool> Save(Guid organizationId, Guid projectId, Guid userId, string tool, int revision, string json, CancellationToken ct)
    {
        if (!ConnectionDocuments.Tools.ContainsKey(tool)) return await Write(organizationId, projectId, userId, tool, revision, json, ct);
        if (db.Database.CurrentTransaction is not null)
            return await SaveAndEnqueue(organizationId, projectId, userId, tool, revision, json, ct);

        var attempts = 0;
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            // Each attempt reconstructs the persisted diff and schedule after a transient failure.
            if (attempts++ > 0) db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var saved = await SaveAndEnqueue(organizationId, projectId, userId, tool, revision, json, ct);
            if (saved) await transaction.CommitAsync(ct);
            return saved;
        });
    }

    private async Task<bool> SaveAndEnqueue(Guid organizationId, Guid projectId, Guid userId, string tool, int revision, string json, CancellationToken ct)
    {
        var previous = await Get(organizationId, projectId, tool, ct);
        if ((previous?.Revision ?? 0) != revision || !await Write(organizationId, projectId, userId, tool, revision, json, ct))
            return false;

        using var nextDocument = JsonDocument.Parse(json);
        using var previousDocument = previous?.ContentJson is { } previousJson ? JsonDocument.Parse(previousJson) : null;
        var changes = ConnectionDocuments.Changes(tool, previousDocument?.RootElement ?? default, nextDocument.RootElement).ToArray();
        foreach (var change in changes)
            await events.Enqueue(organizationId, projectId, userId, change, ct);
        if (tool == "calendar") await calendarSchedule.Sync(organizationId, projectId, nextDocument.RootElement, ct);
        if (changes.Length > 0 || tool == "calendar") await db.SaveChangesAsync(ct);
        return true;
    }

    private async Task<bool> Write(Guid organizationId, Guid projectId, Guid userId, string tool, int revision, string json, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var count = await db.ProjectDocuments.Where(document =>
                document.ProjectId == projectId && document.ToolType == tool && document.Revision == revision &&
                db.Projects.Any(project => project.Id == document.ProjectId && project.OrganizationId == organizationId))
            .ExecuteUpdateAsync(update => update
                .SetProperty(document => document.ContentJson, json)
                .SetProperty(document => document.Revision, revision + 1)
                .SetProperty(document => document.UpdatedByUserId, userId)
                .SetProperty(document => document.UpdatedAtUtc, now), ct);
        if (count != 0) return true;
        if (revision != 0) return false;

        // A new project's first save creates its document. Range locks on the unique
        // project/tool index serialize competing first saves without overwriting either.
        var id = Guid.NewGuid();
        count = await db.Database.ExecuteSqlAsync($"""
            INSERT INTO [ProjectDocuments] ([Id], [ProjectId], [ToolType], [ContentJson], [Revision], [UpdatedByUserId], [UpdatedAtUtc])
            SELECT {id}, [project].[Id], {tool}, {json}, 1, {userId}, {now}
            FROM [Projects] AS [project]
            WHERE [project].[Id] = {projectId} AND [project].[OrganizationId] = {organizationId}
              AND NOT EXISTS (
                  SELECT 1 FROM [ProjectDocuments] WITH (UPDLOCK, HOLDLOCK)
                  WHERE [ProjectId] = {projectId} AND [ToolType] = {tool})
            """, ct);
        return count != 0;
    }
}
