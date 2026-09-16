using DevHub.Api.Realtime;
using DevHub.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Controllers;

[Tags("Workshop presence")]
[Route("api/organizations/{organizationId:guid}/presence")]
[Authorize]
public sealed class WorkshopPresenceController(
    IOrganizationRepository organizationRepository,
    WorkshopPresenceTracker presenceTracker,
    IHubContext<WorkshopHub> hub) : ApiControllerBase
{
    [HttpPost]
    [EndpointName("UpdateWorkshopPresence")]
    [ProducesResponseType<WorkshopPresence[]>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update(
        Guid organizationId,
        [FromBody] UpdateWorkshopPresenceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();

        if (!await organizationRepository.IsMemberAsync(organizationId, userId, cancellationToken)) return NotFound();

        var result = presenceTracker.Heartbeat(organizationId, userId, request.Status);
        if (result.StatusChanged)
            await hub.Clients.Group(WorkshopHub.GetGroupName(organizationId)).SendAsync(
                "MemberStatusChanged", userId.ToString(), result.Status, organizationId.ToString(), cancellationToken);
        return Ok(result.Members);
    }
}

public sealed record UpdateWorkshopPresenceRequest(string Status);
