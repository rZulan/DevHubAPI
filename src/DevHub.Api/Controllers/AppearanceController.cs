using DevHub.Application.Appearance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

/// <summary>The current member's personal workshop color schemes.</summary>
[Authorize]
[Tags("Appearance")]
[Route("api/organizations/{organizationId:guid}/appearance")]
public sealed class AppearanceController(IMemberAppearanceService appearance) : ApiControllerBase
{
    [HttpGet]
    [EndpointName("GetMemberAppearance")]
    [ProducesResponseType<MemberAppearanceResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid organizationId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await appearance.Get(organizationId, userId, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPut("active")]
    [EndpointName("SetActiveColorScheme")]
    [ProducesResponseType<MemberAppearanceResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetActive(Guid organizationId, SetActiveColorSchemeRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await appearance.SetActive(organizationId, userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpPost("schemes")]
    [EndpointName("CreateColorScheme")]
    [ProducesResponseType<ColorSchemeResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid organizationId, SaveColorSchemeRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await appearance.Create(organizationId, userId, request, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : Failure(result.Error!);
    }

    [HttpPut("schemes/{schemeId:guid}")]
    [EndpointName("UpdateColorScheme")]
    [ProducesResponseType<ColorSchemeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid organizationId, Guid schemeId, SaveColorSchemeRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await appearance.Update(organizationId, userId, schemeId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }

    [HttpDelete("schemes/{schemeId:guid}")]
    [EndpointName("DeleteColorScheme")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid organizationId, Guid schemeId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await appearance.Delete(organizationId, userId, schemeId, ct);
        return result.IsSuccess ? NoContent() : Failure(result.Error!);
    }
}
