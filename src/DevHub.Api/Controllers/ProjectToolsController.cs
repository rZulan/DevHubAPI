using System.Text.Json;
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
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("workshop-save")]
    [RequestSizeLimit(8_000_000)]
    public async Task<IActionResult> Save(Guid organizationId, Guid projectId, string tool, SaveToolDocumentRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await tools.Save(organizationId, projectId, userId, tool, request, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        await Publish(organizationId, projectId, tool, result.Value!);
        return Ok(result.Value);
    }

    [HttpPut("initialize")]
    [Microsoft.AspNetCore.RateLimiting.EnableRateLimiting("workshop-save")]
    public async Task<IActionResult> Initialize(Guid organizationId, Guid projectId, string tool, InitializeSdlcRequest request, CancellationToken ct)
    {
        if (tool != "sdlc") return NotFound();
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await tools.InitializeSdlc(organizationId, projectId, userId, request, ct);
        if (!result.IsSuccess) return Failure(result.Error!);
        await Publish(organizationId, projectId, tool, result.Value!);
        return Ok(result.Value);
    }

    private async Task Publish(Guid organizationId, Guid projectId, string tool, ToolDocumentResponse result)
    {
        await realtime.Revalidate(organizationId, projectId);
        var clients = hub.Clients.Group(WorkshopHub.GetProjectToolsGroupName(organizationId, projectId));
        await clients.SendAsync("ProjectToolChanged", projectId.ToString(), tool, result.Revision);
        if (tool == "sdlc" && result.Document is { ValueKind: JsonValueKind.Object } document &&
            document.TryGetProperty("connectToTodo", out var enabled) && enabled.ValueKind == JsonValueKind.True)
            await clients.SendAsync("ProjectToolChanged", projectId.ToString(), "todo", 0);
    }
}
