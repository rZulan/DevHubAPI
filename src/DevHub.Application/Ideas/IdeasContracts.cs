using System.Text.Json;
using DevHub.Application.Common;

namespace DevHub.Application.Ideas;

public sealed record IdeasResponse(int Revision, JsonElement Items);
public sealed record SaveIdeasRequest(int Revision, JsonElement Items);

public interface IIdeasService
{
    Task<Result<IdeasResponse>> Get(Guid organizationId, Guid projectId, Guid userId, CancellationToken ct);
    Task<Result<IdeasResponse>> Save(Guid organizationId, Guid projectId, Guid userId, SaveIdeasRequest request, CancellationToken ct);
}
