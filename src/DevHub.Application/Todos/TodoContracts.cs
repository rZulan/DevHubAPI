using System.ComponentModel.DataAnnotations;
using DevHub.Application.Common;

namespace DevHub.Application.Todos;

public sealed record CreateTodoRequest([Required, StringLength(150)] string Name,
    [StringLength(2000)] string? Description, DateOnly? TargetDate);
public sealed record CreateTodoTaskRequest([Required, StringLength(150)] string Title,
    [StringLength(2000)] string? Description, [Required, RegularExpression("^(pending|progress|done)$")] string Status);
public sealed record MoveTodoTaskRequest([Required, RegularExpression("^(pending|progress|done)$")] string Status);
public sealed record TodoTaskResponse(Guid Id, Guid TodoId, Guid ProjectId, string Title, string Description, string Status);
public sealed record TodoResponse(Guid Id, Guid ProjectId, string Name, string Description, DateOnly? TargetDate,
    DateTimeOffset CreatedAtUtc, IReadOnlyList<TodoTaskResponse> Tasks);

public interface ITodoService
{
    Task<Result<TodoPageResponse>> ListPage(Guid organizationId, Guid projectId, Guid userId, int page, CancellationToken ct, int pageSize = 12);
    Task<Result<TodoResponse>> Get(Guid organizationId, Guid projectId, Guid todoId, Guid userId, CancellationToken ct);
    Task<Result<IReadOnlyList<TodoResponse>>> List(Guid organizationId, Guid projectId, Guid userId, CancellationToken ct);
    Task<Result<TodoResponse>> Create(Guid organizationId, Guid projectId, Guid userId, CreateTodoRequest request, CancellationToken ct);
    Task<Result<TodoTaskResponse>> AddTask(Guid organizationId, Guid projectId, Guid todoId, Guid userId, CreateTodoTaskRequest request, CancellationToken ct);
    Task<Result<TodoTaskResponse>> MoveTask(Guid organizationId, Guid projectId, Guid todoId, Guid taskId, Guid userId, MoveTodoTaskRequest request, CancellationToken ct);
}

public sealed record TodoPageResponse(IReadOnlyList<TodoResponse> Items, bool HasMore);
