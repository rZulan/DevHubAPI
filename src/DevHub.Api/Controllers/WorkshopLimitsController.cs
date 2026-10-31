using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Workshops;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[Authorize]
[Route("api/organizations/{organizationId:guid}/limits")]
public sealed class WorkshopLimitsController(IOrganizationRepository organizations, IWorkshopStorage storage, WorkshopLimits limits) : ApiControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid organizationId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        if (!await organizations.IsMemberAsync(organizationId, userId, ct)) return NotFound();
        return Ok(new {storage = await storage.Get(organizationId, ct), limits.CanvasWarningObjects, limits.CanvasMaximumObjects,
            limits.SavesPerMinute, limits.CreatesPerMinute, limits.InvitesPerUserPerDay, limits.InvitesPerWorkshopPerDay,
            limits.MaximumMentions, limits.ChatMessagesPerMinute, limits.NotificationsPerMinute});
    }
}
