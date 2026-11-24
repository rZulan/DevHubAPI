using System.Text.Json.Serialization;
using DevHub.Api.Realtime;
using DevHub.Application.Common;
using DevHub.Application.Connections;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DevHub.Api.Controllers;

[Authorize]
[Tags("Project connections")]
[Route("api/organizations/{organizationId:guid}/projects/{projectId:guid}/connections")]
public sealed class ProjectConnectionsController(IProjectConnections connections, WorkshopRealtime realtime) : ApiControllerBase
{
    public sealed record SetTodoCalendarRequest([property: JsonRequired] bool Enabled);

    [HttpGet]
    public Task<IActionResult> List(Guid organizationId, Guid projectId, CancellationToken ct) =>
        WithScope(organizationId, projectId, scope => connections.List(scope, ct));

    [HttpGet("access")]
    public Task<IActionResult> Access(Guid organizationId, Guid projectId, CancellationToken ct) =>
        WithScope(organizationId, projectId, scope => connections.Access(scope, ct));

    [HttpPut("todo-calendar")]
    [EnableRateLimiting("workshop-save")]
    public Task<IActionResult> SetTodoCalendar(Guid organizationId, Guid projectId, SetTodoCalendarRequest request, CancellationToken ct) =>
        WithScope(organizationId, projectId, async scope =>
        {
            var result = await connections.SetTodoCalendarEnabled(scope, request.Enabled, ct);
            if (result.IsSuccess) await realtime.Changed(organizationId);
            return result;
        });

    [HttpGet("catalog")]
    public Task<IActionResult> Catalog(Guid organizationId, Guid projectId, CancellationToken ct) =>
        WithScope(organizationId, projectId, scope => connections.Catalog(scope, ct));

    [HttpGet("nodes")]
    public Task<IActionResult> Nodes(Guid organizationId, Guid projectId, [FromQuery] string? q, [FromQuery] string? kind,
        [FromQuery] string? id, [FromQuery] string? parentId, CancellationToken ct, [FromQuery] int page = 1) =>
        WithScope(organizationId, projectId, scope => connections.FindNodes(scope, q, kind, id, parentId, page, ct));

    [HttpGet("runs")]
    public Task<IActionResult> Runs(Guid organizationId, Guid projectId, [FromQuery] Guid? connectionId, CancellationToken ct, [FromQuery] int page = 1) =>
        WithScope(organizationId, projectId, scope => connections.Runs(scope, connectionId, page, ct));

    [HttpGet("{id:guid}")]
    public Task<IActionResult> Get(Guid organizationId, Guid projectId, Guid id, CancellationToken ct) =>
        WithScope(organizationId, projectId, scope => connections.Get(scope, id, ct));

    [HttpPost]
    [EnableRateLimiting("workshop-save")]
    public Task<IActionResult> Create(Guid organizationId, Guid projectId, SaveConnectionRequest request, CancellationToken ct) =>
        WithScope(organizationId, projectId, scope => connections.Save(scope, null, request, ct));

    [HttpPost("validate")]
    [EnableRateLimiting("workshop-save")]
    public Task<IActionResult> Validate(Guid organizationId, Guid projectId, [FromQuery] Guid? id, SaveConnectionRequest request, CancellationToken ct) =>
        WithScope(organizationId, projectId, scope => connections.Validate(scope, id, request, ct));

    [HttpPut("{id:guid}")]
    [EnableRateLimiting("workshop-save")]
    public Task<IActionResult> Save(Guid organizationId, Guid projectId, Guid id, SaveConnectionRequest request, CancellationToken ct) =>
        WithScope(organizationId, projectId, scope => connections.Save(scope, id, request, ct));

    [HttpDelete("{id:guid}")]
    [EnableRateLimiting("workshop-save")]
    public Task<IActionResult> Delete(Guid organizationId, Guid projectId, Guid id, [FromQuery] int revision, CancellationToken ct) =>
        WithScope(organizationId, projectId, scope => connections.Delete(scope, id, revision, ct));

    private async Task<IActionResult> WithScope<T>(Guid organizationId, Guid projectId, Func<ConnectionScope, Task<Result<T>>> work)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await work(new(organizationId, projectId, userId));
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
}
