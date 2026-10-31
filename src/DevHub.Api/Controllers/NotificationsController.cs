using DevHub.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[Tags("Notifications")]
[Route("api/organizations/{organizationId:guid}/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notifications) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationId, [FromQuery] long? before, [FromQuery] bool unreadOnly, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await notifications.List(organizationId, userId, before, unreadOnly, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpPut("{id:long}/read")]
    public async Task<IActionResult> Read(Guid organizationId, long id, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await notifications.MarkRead(organizationId, userId, id, ct);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }
    [HttpPut("read-all")]
    public async Task<IActionResult> ReadAll(Guid organizationId, MarkNotificationsReadRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await notifications.MarkAllRead(organizationId, userId, request.ThroughId, ct);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }
}
