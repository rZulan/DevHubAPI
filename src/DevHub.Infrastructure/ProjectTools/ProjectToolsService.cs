using System.Text.Json;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Ideas;
using DevHub.Application.ProjectTools;
using DevHub.Domain.Organizations;

namespace DevHub.Infrastructure.ProjectTools;

internal sealed class ProjectToolsService(ProjectDocumentStore documents, IIdeasService ideas, IOrganizationRepository organizations,
    DevHub.Application.Workshops.WorkshopLimits limits, DevHub.Application.Workshops.IWorkshopUsage usage) : IProjectToolsService
{
    private static readonly Error Missing = new("Tools.NotFound", "The project tool was not found.", ErrorType.NotFound);
    private static readonly Error Conflict = new("Tools.Conflict", "Another session saved changes. Reload before editing again.", ErrorType.Conflict);
    private static bool Known(string tool) => tool is "erd" or "ui-editor" or "sdlc" or "notes" or "wiki" or "calendar" or "uml";
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
            "uml" => OrganizationPermissions.EditUmlDiagrams,
            "notes" => OrganizationPermissions.ManageNotes,
            "wiki" => OrganizationPermissions.ManageWiki,
            "calendar" => OrganizationPermissions.ManageTasks,
            _ => OrganizationPermissions.ManageRoadmap
        };
        if (organization is null || !organization.HasPermission(userId, permission))
            return Result<ToolDocumentResponse>.Failure(new("Tools.Forbidden", $"You need {permission} permission to update this document.", ErrorType.Forbidden));
        if (request.Revision < 0 || request.Revision == int.MaxValue || !ToolDocumentValidation.Valid(tool, request.Document))
            return Result<ToolDocumentResponse>.Failure(new("Tools.Invalid", "The document contains invalid properties, references or exceeds the supported size.", ErrorType.Validation));
        if (tool == "sdlc")
        {
            var previous = await documents.Get(organizationId, projectId, tool, ct);
            if ((previous?.Revision ?? 0) != request.Revision)
                return Result<ToolDocumentResponse>.Failure(Conflict);
            if (previous?.ContentJson is not { } savedRoadmap)
                return Result<ToolDocumentResponse>.Failure(new("Tools.RoadmapUninitialized", "Initialize the roadmap before saving changes.", ErrorType.Validation));
            var prior = JsonSerializer.Deserialize<JsonElement>(savedRoadmap);
            if (prior.GetProperty("model").GetString() != request.Document.GetProperty("model").GetString())
                return Result<ToolDocumentResponse>.Failure(new("Tools.RoadmapModelLocked", "Start a new roadmap to change the SDLC model.", ErrorType.Validation));
            if (!SdlcCustomization.TryPrepareSave(prior, request.Document, userId, out var roadmap))
                return Result<ToolDocumentResponse>.Failure(new("Tools.Forbidden", "Only the roadmap creator can change its deliverable lists.", ErrorType.Forbidden));
            request = request with { Document = roadmap };
        }
        if (tool == "uml")
        {
            var previous = await documents.Get(organizationId, projectId, tool, ct);
            if ((previous?.Revision ?? 0) != request.Revision)
                return Result<ToolDocumentResponse>.Failure(new("Tools.Conflict", "Another session saved changes. Reload before editing again.", ErrorType.Conflict));
            var old = previous?.ContentJson is { } prior ? JsonSerializer.Deserialize<JsonElement>(prior).GetProperty("diagrams").EnumerateArray().ToDictionary(d => d.GetProperty("id").GetString()!, d => d.GetProperty("items").GetArrayLength()) : new Dictionary<string,int>();
            foreach (var diagram in request.Document.GetProperty("diagrams").EnumerateArray())
                if (diagram.GetProperty("items").GetArrayLength() > Math.Max(old.GetValueOrDefault(diagram.GetProperty("id").GetString()!), limits.CanvasMaximumObjects))
                    return Result<ToolDocumentResponse>.Failure(new("Workshop.CanvasCapacity", $"Each UML canvas supports {limits.CanvasMaximumObjects:N0} elements.", ErrorType.Validation));
        }
        if (tool is "ui-editor" or "notes")
        {
            var previous = await documents.Get(organizationId, projectId, tool, ct);
            if ((previous?.Revision ?? 0) != request.Revision)
                return Result<ToolDocumentResponse>.Failure(new("Tools.Conflict", "Another session saved changes. Reload before editing again.", ErrorType.Conflict));
            var property = tool == "ui-editor" ? "layers" : "notes";
            var items = request.Document.GetProperty(property);
            var oldItems = previous?.ContentJson is not { } previousJson ? JsonSerializer.Deserialize<JsonElement>("[]") : JsonSerializer.Deserialize<JsonElement>(previousJson).GetProperty(property);
            if (tool == "ui-editor" && items.GetArrayLength() > Math.Max(limits.CanvasMaximumObjects, oldItems.GetArrayLength()))
                return Result<ToolDocumentResponse>.Failure(new("Workshop.CanvasCapacity", $"This design supports {limits.CanvasMaximumObjects:N0} objects. Existing larger designs can be edited or reduced.", ErrorType.Validation));
            if (tool == "notes")
            {
                var oldIds = oldItems.EnumerateArray().Select(item => item.GetProperty("id").GetString()).ToHashSet();
                var created = items.EnumerateArray().Count(item => !oldIds.Contains(item.GetProperty("id").GetString()));
                if (created > limits.CreatesPerMinute)
                    return Result<ToolDocumentResponse>.Failure(new("Workshop.BatchCapacity", $"Add at most {limits.CreatesPerMinute} new notes in one save. Your draft remains available to export.", ErrorType.Validation));
                if (created > 0 && await usage.Reserve([new($"create-user:{userId:N}", limits.CreatesPerMinute, created, TimeSpan.FromMinutes(1))], ct) is { } quotaError)
                    return Result<ToolDocumentResponse>.Failure(quotaError);
            }
        }
        var saved = await documents.Save(organizationId, projectId, userId, tool, request.Revision, request.Document.GetRawText(), ct);
        return !saved
            ? Result<ToolDocumentResponse>.Failure(Conflict)
            : Result<ToolDocumentResponse>.Success(new(request.Revision + 1, request.Document));
    }

    public async Task<Result<ToolDocumentResponse>> InitializeSdlc(Guid organizationId, Guid projectId, Guid userId, InitializeSdlcRequest request, CancellationToken ct)
    {
        if (!await ideas.CanView(organizationId, projectId, userId, ct)) return Result<ToolDocumentResponse>.Failure(Missing);
        var organization = await organizations.GetByIdAsync(organizationId, ct);
        if (organization?.HasPermission(userId, OrganizationPermissions.ManageRoadmap) != true)
            return Result<ToolDocumentResponse>.Failure(new("Tools.Forbidden", "You need Manage roadmap permission to initialize this roadmap.", ErrorType.Forbidden));
        if (request.ConnectToTodo && !organization.HasPermission(userId, OrganizationPermissions.ManageTasks))
            return Result<ToolDocumentResponse>.Failure(new("Tools.Forbidden", "You need Manage tasks permission to connect this roadmap to To-do.", ErrorType.Forbidden));
        if (request.Revision is < 0 or int.MaxValue || request.Model is not ("waterfall" or "v-model" or "agile" or "iterative"))
            return Result<ToolDocumentResponse>.Failure(new("Tools.Invalid", "Choose a supported SDLC model and a valid revision.", ErrorType.Validation));
        var prior = await documents.Get(organizationId, projectId, "sdlc", ct);
        if ((prior?.Revision ?? 0) != request.Revision) return Result<ToolDocumentResponse>.Failure(Conflict);
        if (await usage.Reserve([new($"create-user:{userId:N}", limits.CreatesPerMinute, 1, TimeSpan.FromMinutes(1))], ct) is { } quotaError)
            return Result<ToolDocumentResponse>.Failure(quotaError);

        var document = JsonSerializer.SerializeToElement(new
        {
            model = request.Model,
            currentPhase = 0,
            completed = false,
            notes = new Dictionary<string, string>(),
            creatorId = userId,
            roadmapId = Guid.NewGuid(),
            connectToTodo = request.ConnectToTodo
        });
        return await documents.Save(organizationId, projectId, userId, "sdlc", request.Revision, document.GetRawText(), ct)
            ? Result<ToolDocumentResponse>.Success(new(request.Revision + 1, document))
            : Result<ToolDocumentResponse>.Failure(Conflict);
    }
}
