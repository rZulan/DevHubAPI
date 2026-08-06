using Microsoft.AspNetCore.Mvc;
using ProjectRoadMapper.Api.Errors;
using ProjectRoadMapper.Application.Common;

namespace ProjectRoadMapper.Api.Controllers;

[ApiController]
public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult Failure(Error error) =>
        ApiProblemDetails.CreateResult(error, HttpContext);
}
