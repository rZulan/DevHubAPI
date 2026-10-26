using System.Text.Json;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Ideas;
using DevHub.Application.ProjectTools;
using DevHub.Domain.Organizations;

namespace DevHub.Infrastructure.ProjectTools;

internal sealed class ProjectToolsService(ProjectDocumentStore documents, IIdeasService ideas, IOrganizationRepository organizations) : IProjectToolsService
{
    private static readonly Error Missing = new("Tools.NotFound", "The project tool was not found.", ErrorType.NotFound);
    private static bool Known(string tool) => tool is "erd" or "ui-editor" or "sdlc" or "notes" or "wiki";
    public async Task<Result<ToolDocumentResponse>> Get(Guid organizationId, Guid projectId, Guid userId, string tool, CancellationToken ct)
    {
        if (!Known(tool) || !await ideas.CanView(organizationId, projectId, userId, ct)) return Result<ToolDocumentResponse>.Failure(Missing);
        var document = await documents.Get(organizationId, projectId, tool, ct);
        return Result<ToolDocumentResponse>.Success(new(document?.Revision ?? 0,
            document?.ContentJson is { } json ? JsonSerializer.Deserialize<JsonElement>(json) : null));
    }
    public async Task<Result<ToolDocumentResponse>> Save(Guid organizationId, Guid projectId, Guid userId, string tool, SaveToolDocumentRequest request, CancellationToken ct)
    {
        if (!Known(tool) || !await ideas.CanView(organizationId, projectId, userId, ct)) return Result<ToolDocumentResponse>.Failure(Missing);
        var organization = await organizations.GetByIdAsync(organizationId, ct);
        var permission = tool switch
        {
            "erd" => OrganizationPermissions.EditErd,
            "ui-editor" => OrganizationPermissions.EditUiDesigns,
            "notes" => OrganizationPermissions.ManageNotes,
            "wiki" => OrganizationPermissions.ManageWiki,
            _ => OrganizationPermissions.ManageRoadmap
        };
        if (organization is null || !organization.HasPermission(userId, permission))
            return Result<ToolDocumentResponse>.Failure(new("Tools.Forbidden", $"You need {permission} permission to update this document.", ErrorType.Forbidden));
        if (request.Revision < 0 || request.Revision == int.MaxValue || !ToolDocumentValidation.Valid(tool, request.Document))
            return Result<ToolDocumentResponse>.Failure(new("Tools.Invalid", "The document contains invalid properties, references or exceeds the supported size.", ErrorType.Validation));
        var json = request.Document.GetRawText();
        var saved = await documents.Save(organizationId, projectId, userId, tool, request.Revision, json, ct);
        return !saved
            ? Result<ToolDocumentResponse>.Failure(new("Tools.Conflict", "Another session saved changes. Reload before editing again.", ErrorType.Conflict))
            : Result<ToolDocumentResponse>.Success(new(request.Revision + 1, request.Document));
    }
}
