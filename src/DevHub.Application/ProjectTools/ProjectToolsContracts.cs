using System.Text.Json;
using DevHub.Application.Common;

namespace DevHub.Application.ProjectTools;

public sealed record ToolDocumentResponse(int Revision, JsonElement? Document);
public sealed record SaveToolDocumentRequest(int Revision, JsonElement Document);
public interface IProjectToolsService
{
    Task<Result<ToolDocumentResponse>> Get(Guid organizationId, Guid projectId, Guid userId, string tool, CancellationToken ct);
    Task<Result<ToolDocumentResponse>> Save(Guid organizationId, Guid projectId, Guid userId, string tool, SaveToolDocumentRequest request, CancellationToken ct);
}
