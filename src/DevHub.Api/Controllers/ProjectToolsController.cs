using DevHub.Application.ProjectTools;
using DevHub.Api.Realtime;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Controllers;

[Authorize]
[Tags("Project tools")]
[Route("api/organizations/{organizationId:guid}/projects/{projectId:guid}/tools/{tool}")]
public sealed class ProjectToolsController(IProjectToolsService tools, IHubContext<WorkshopHub> hub, WorkshopRealtime realtime) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId, Guid projectId, string tool, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await tools.Get(organizationId, projectId, userId, tool, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpPut]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> Save(Guid organizationId, Guid projectId, string tool, SaveToolDocumentRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await tools.Save(organizationId, projectId, userId, tool, request, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        await realtime.Revalidate(organizationId, projectId);
        await hub.Clients.Group(WorkshopHub.GetProjectToolsGroupName(organizationId, projectId)).SendAsync(
            "ProjectToolChanged", projectId.ToString(), tool, result.Value!.Revision);
        return Ok(result.Value);
    }
}
