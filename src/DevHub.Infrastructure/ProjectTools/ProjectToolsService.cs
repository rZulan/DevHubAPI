using System.Text.Json;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Ideas;
using DevHub.Application.ProjectTools;
using DevHub.Domain.Organizations;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.ProjectTools;

internal sealed class ProjectToolsService(ApplicationDbContext db, IIdeasService ideas, IOrganizationRepository organizations) : IProjectToolsService
{
    private static readonly Error Missing = new("Tools.NotFound", "The project tool was not found.", ErrorType.NotFound);
    private static bool Known(string tool) => tool is "erd" or "ui-editor" or "sdlc" or "notes";
    public async Task<Result<ToolDocumentResponse>> Get(Guid organizationId, Guid projectId, Guid userId, string tool, CancellationToken ct)
    {
        if (!Known(tool) || !await ideas.CanView(organizationId, projectId, userId, ct)) return Result<ToolDocumentResponse>.Failure(Missing);
        var project = await db.Projects.AsNoTracking().SingleAsync(p => p.Id == projectId && p.OrganizationId == organizationId, ct);
        var (json, revision) = tool switch
        {
            "erd" => (project.ErdJson, project.ErdRevision),
            "ui-editor" => (project.DesignJson, project.DesignRevision),
            "notes" => (project.NotesJson, project.NotesRevision),
            _ => (project.RoadmapJson, project.RoadmapRevision)
        };
        return Result<ToolDocumentResponse>.Success(new(revision, json is null ? null : JsonSerializer.Deserialize<JsonElement>(json)));
    }
    public async Task<Result<ToolDocumentResponse>> Save(Guid organizationId, Guid projectId, Guid userId, string tool, SaveToolDocumentRequest request, CancellationToken ct)
    {
        if (!Known(tool) || !await ideas.CanView(organizationId, projectId, userId, ct)) return Result<ToolDocumentResponse>.Failure(Missing);
        var organization = await organizations.GetByIdAsync(organizationId, ct);
        var permission = tool == "sdlc" ? OrganizationPermissions.ManageProjects : OrganizationPermissions.EditIdeation;
        if (organization is null || !organization.HasPermission(userId, permission))
            return Result<ToolDocumentResponse>.Failure(new("Tools.Forbidden", tool == "sdlc" ? "You need Manage projects permission to update the roadmap." : "You need Edit ideation permission to update this document.", ErrorType.Forbidden));
        if (request.Revision < 0 || request.Revision == int.MaxValue || !ToolDocumentValidation.Valid(tool, request.Document))
            return Result<ToolDocumentResponse>.Failure(new("Tools.Invalid", "The document contains invalid properties, references or exceeds the supported size.", ErrorType.Validation));
        var query = db.Projects.Where(p => p.Id == projectId && p.OrganizationId == organizationId);
        var json = request.Document.GetRawText();
        var count = tool switch
        {
            "erd" => await query.Where(p => p.ErdRevision == request.Revision).ExecuteUpdateAsync(update => update.SetProperty(p => p.ErdJson, json).SetProperty(p => p.ErdRevision, request.Revision + 1), ct),
            "ui-editor" => await query.Where(p => p.DesignRevision == request.Revision).ExecuteUpdateAsync(update => update.SetProperty(p => p.DesignJson, json).SetProperty(p => p.DesignRevision, request.Revision + 1), ct),
            "notes" => await query.Where(p => p.NotesRevision == request.Revision).ExecuteUpdateAsync(update => update.SetProperty(p => p.NotesJson, json).SetProperty(p => p.NotesRevision, request.Revision + 1), ct),
            _ => await query.Where(p => p.RoadmapRevision == request.Revision).ExecuteUpdateAsync(update => update.SetProperty(p => p.RoadmapJson, json).SetProperty(p => p.RoadmapRevision, request.Revision + 1), ct)
        };
        return count == 0
            ? Result<ToolDocumentResponse>.Failure(new("Tools.Conflict", "Another session saved changes. Reload before editing again.", ErrorType.Conflict))
            : Result<ToolDocumentResponse>.Success(new(request.Revision + 1, request.Document));
    }
}
