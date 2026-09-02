using DevHub.Api.Realtime;
using DevHub.Application.Abstractions.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[Tags("Workshop presence")]
[Route("api/organizations/{organizationId:guid}/presence")]
[Authorize]
public sealed class WorkshopPresenceController(
    IOrganizationRepository organizationRepository,
    WorkshopPresenceTracker presenceTracker) : ApiControllerBase
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

        var organization = await organizationRepository.GetByIdAsync(
            organizationId,
            cancellationToken);
        if (organization is null || !organization.HasMember(userId)) return NotFound();

        return Ok(presenceTracker.Heartbeat(organizationId, userId, request.Status));
    }
}

public sealed record UpdateWorkshopPresenceRequest(string Status);
