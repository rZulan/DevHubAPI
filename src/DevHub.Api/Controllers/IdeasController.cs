using DevHub.Application.Ideas;
using DevHub.Api.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Controllers;

[Authorize]
[Tags("Ideas")]
[Route("api/organizations/{organizationId:guid}/projects/{projectId:guid}/ideas")]
public sealed class IdeasController(IIdeasService ideas, IHubContext<WorkshopHub> hub) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId, Guid projectId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await ideas.Get(organizationId, projectId, userId, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> Save(Guid organizationId, Guid projectId, SaveIdeasRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await ideas.Save(organizationId, projectId, userId, request, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        await hub.Clients.Group(WorkshopHub.GetIdeasGroupName(organizationId, projectId)).SendAsync(
            "IdeasDocumentChanged", projectId.ToString(), result.Value, userId.ToString(), ct);
        return Ok(result.Value);
    }
}
