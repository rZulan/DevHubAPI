using Microsoft.AspNetCore.Mvc;
using DevHub.Api.Errors;
using DevHub.Application.Common;

namespace DevHub.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult Failure(Error error) =>
        ApiProblemDetails.CreateResult(error, HttpContext);
}
