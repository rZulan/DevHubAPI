using DevHub.Api.Security;
using DevHub.Application.Administration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[Tags("Platform administration")]
[Authorize(Policy = PlatformAdminAccess.Policy)]
[Route("api/admin")]
public sealed class PlatformAdminController(IPlatformOverview overview) : ApiControllerBase
{
    [HttpGet("overview")]
    public Task<PlatformOverview> GetOverview(CancellationToken cancellationToken) =>
        overview.GetAsync(cancellationToken);
}
