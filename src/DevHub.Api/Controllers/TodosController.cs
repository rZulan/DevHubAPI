using DevHub.Application.Todos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DevHub.Api.Controllers;

[Authorize]
[Tags("TODOs")]
[Route("api/organizations/{organizationId:guid}/projects/{projectId:guid}/todos")]
public sealed class TodosController(ITodoService todos) : ApiControllerBase
{
    [HttpGet("pages")]
    public async Task<IActionResult> ListPage(Guid organizationId, Guid projectId, [FromQuery] int page, CancellationToken ct, [FromQuery] int pageSize = 12)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await todos.ListPage(organizationId, projectId, userId, page, ct, pageSize);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpGet("{todoId:guid}")]
    public async Task<IActionResult> Get(Guid organizationId, Guid projectId, Guid todoId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await todos.Get(organizationId, projectId, todoId, userId, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpGet]
    public async Task<IActionResult> List(Guid organizationId, Guid projectId, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await todos.List(organizationId, projectId, userId, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
    [HttpPost]
    public async Task<IActionResult> Create(Guid organizationId, Guid projectId, CreateTodoRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await todos.Create(organizationId, projectId, userId, request, ct);
        return result.IsSuccess ? StatusCode(201, result.Value) : Failure(result.Error!);
    }
    [HttpPost("{todoId:guid}/tasks")]
    public async Task<IActionResult> AddTask(Guid organizationId, Guid projectId, Guid todoId, CreateTodoTaskRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await todos.AddTask(organizationId, projectId, todoId, userId, request, ct);
        return result.IsSuccess ? StatusCode(201, result.Value) : Failure(result.Error!);
    }
    [HttpPatch("{todoId:guid}/tasks/{taskId:guid}")]
    public async Task<IActionResult> MoveTask(Guid organizationId, Guid projectId, Guid todoId, Guid taskId, MoveTodoTaskRequest request, CancellationToken ct)
    {
        if (!TryGetAuthenticatedUserId(out var userId)) return InvalidAuthenticatedUser();
        var result = await todos.MoveTask(organizationId, projectId, todoId, taskId, userId, request, ct);
        return result.IsSuccess ? Ok(result.Value) : Failure(result.Error!);
    }
}
