using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using DevHub.Api.Errors;
using DevHub.Application.Common;

namespace DevHub.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult Failure(Error error) =>
        ApiProblemDetails.CreateResult(error, HttpContext);

    protected bool TryGetAuthenticatedUserId(out Guid userId) =>
        Guid.TryParse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out userId);

    protected ObjectResult InvalidAuthenticatedUser() => Failure(new Error(
        "Authentication.InvalidSubject",
        "The authenticated identity does not contain a valid user identifier.",
        ErrorType.Unauthorized));
}
