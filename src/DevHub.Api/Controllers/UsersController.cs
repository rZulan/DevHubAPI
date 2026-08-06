using System.IdentityModel.Tokens.Jwt;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using DevHub.Application.Common;
using DevHub.Application.Users;
using DevHub.Application.Users.Queries.GetUserById;

namespace DevHub.Api.Controllers;

/// <summary>
/// Retrieves user profile information.
/// </summary>
[Tags("Users")]
[Route("api/users")]
[Authorize]
public sealed class UsersController(ISender sender) : ApiControllerBase
{
    /// <summary>
    /// Profile
    /// </summary>
    /// <remarks>
    /// Resolves the user from the authenticated cookie or JWT bearer token and returns
    /// the corresponding profile.
    /// </remarks>
    /// <returns>The authenticated user's profile.</returns>
    /// <response code="200">The current user's profile was found.</response>
    /// <response code="401">The request is unauthenticated or contains an invalid user identifier.</response>
    /// <response code="404">The authenticated user no longer exists.</response>
    [HttpGet("me")]
    [EndpointName("GetCurrentUser")]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        var subject = User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

        if (!Guid.TryParse(subject, out var userId))
        {
            return Failure(new Error(
                "Authentication.InvalidSubject",
                "The authenticated identity does not contain a valid user identifier.",
                ErrorType.Unauthorized));
        }

        var result = await sender.Send(new GetUserByIdQuery(userId), cancellationToken);

        return result.IsSuccess
            ? Ok(result.Value)
            : Failure(result.Error!);
    }
}
