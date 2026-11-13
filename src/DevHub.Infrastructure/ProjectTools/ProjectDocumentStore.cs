using DevHub.Domain.Projects;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.ProjectTools;

internal sealed class ProjectDocumentStore(ApplicationDbContext db)
{
    public Task<ProjectDocument?> Get(Guid organizationId, Guid projectId, string tool, CancellationToken ct) =>
        db.ProjectDocuments.AsNoTracking().SingleOrDefaultAsync(document =>
            document.ProjectId == projectId && document.ToolType == tool &&
            db.Projects.Any(project => project.Id == document.ProjectId && project.OrganizationId == organizationId), ct);

    public async Task<bool> Save(Guid organizationId, Guid projectId, Guid userId, string tool, int revision, string json, CancellationToken ct)
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
