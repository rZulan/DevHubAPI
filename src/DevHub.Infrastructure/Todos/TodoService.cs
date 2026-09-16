using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Todos;
using DevHub.Domain.Organizations;
using DevHub.Domain.Todos;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Todos;

internal sealed class TodoService(ApplicationDbContext db, IOrganizationRepository organizations, TimeProvider clock) : ITodoService
{
    private static readonly Error Missing = new("Todos.NotFound", "The TODO or project was not found.", ErrorType.NotFound);
    private static readonly Error Forbidden = new("Todos.Forbidden", "You need Manage tasks permission to make this change.", ErrorType.Forbidden);
    private async Task<Error?> CheckAccess(Guid organizationId, Guid projectId, Guid userId, bool write, CancellationToken ct)
    {
        var organization = await organizations.GetByIdAsync(organizationId, ct);
        if (organization is null || !organization.HasMember(userId)) return Missing;
        var project = await db.Projects.SingleOrDefaultAsync(x => x.Id == projectId && x.OrganizationId == organizationId, ct);
        if (project is null || !(organization.HasPermission(userId, OrganizationPermissions.ViewAllProjects) ||
            organization.Teams.Any(t => t.Id == project.TeamId && t.HasMember(userId)))) return Missing;
        return write && !organization.HasPermission(userId, OrganizationPermissions.ManageTasks) ? Forbidden : null;
    }
    private static Error? Validate(object request)
    {
        var errors = new Dictionary<string, string[]>();
        void Text(string key, string? value, int max, bool required = false)
        {
            if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > max)
                errors[key] = [$"Enter {(required ? "a non-empty value of " : "")}at most {max} characters."];
        }
        void Status(string? value)
        {
            if (value is not ("pending" or "progress" or "done")) errors["Status"] = ["Choose pending, progress, or done."];
        }
        switch (request)
        {
            case CreateTodoRequest todo:
                Text("Name", todo.Name, 150, true);
                Text("Description", todo.Description, 2000);
                break;
            case CreateTodoTaskRequest task:
                Text("Title", task.Title, 150, true);
                Text("Description", task.Description, 2000);
                Status(task.Status);
                break;
            case MoveTodoTaskRequest move:
                Status(move.Status);
                break;
        }
        return errors.Count == 0 ? null : new Error("Todos.Invalid", "Check the TODO fields.", ErrorType.Validation, errors);
    }
    private static TodoTaskResponse Map(TodoTask task, Guid projectId) => new(task.Id, task.TodoId, projectId, task.Title, task.Description, task.Status);
    private static TodoResponse Map(Todo todo) => new(todo.Id, todo.ProjectId, todo.Name, todo.Description, todo.TargetDate,
        todo.CreatedAtUtc, todo.Tasks.OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id).Select(t => Map(t, todo.ProjectId)).ToArray());
    public async Task<Result<IReadOnlyList<TodoResponse>>> List(Guid organizationId, Guid projectId, Guid userId, CancellationToken ct)
    {
        if (await CheckAccess(organizationId, projectId, userId, false, ct) is { } error) return Result<IReadOnlyList<TodoResponse>>.Failure(error);
        var todos = await db.Set<Todo>().AsNoTracking().Include(t => t.Tasks).Where(t => t.ProjectId == projectId).OrderByDescending(t => t.CreatedAtUtc).ToListAsync(ct);
        return Result<IReadOnlyList<TodoResponse>>.Success(todos.Select(Map).ToArray());
    }
    public async Task<Result<TodoPageResponse>> ListPage(Guid organizationId, Guid projectId, Guid userId, int page, CancellationToken ct, int pageSize = 12)
    {
        if (await CheckAccess(organizationId, projectId, userId, false, ct) is { } error) return Result<TodoPageResponse>.Failure(error);
        if (pageSize < 1 || pageSize > 100 || page < 0 || page > int.MaxValue / pageSize)
            return Result<TodoPageResponse>.Failure(new Error("Todos.InvalidPage", "Choose a valid page.", ErrorType.Validation));
        var items = await db.Set<Todo>().AsNoTracking().Include(t => t.Tasks)
            .Where(t => t.ProjectId == projectId).OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.Id)
            .Skip(page * pageSize).Take(pageSize + 1).ToListAsync(ct);
        return Result<TodoPageResponse>.Success(new(items.Take(pageSize).Select(Map).ToArray(), items.Count > pageSize));
    }
    public async Task<Result<TodoResponse>> Get(Guid organizationId, Guid projectId, Guid todoId, Guid userId, CancellationToken ct)
    {
        if (await CheckAccess(organizationId, projectId, userId, false, ct) is { } error) return Result<TodoResponse>.Failure(error);
        var todo = await db.Set<Todo>().AsNoTracking().Include(t => t.Tasks)
            .SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        return todo is null ? Result<TodoResponse>.Failure(Missing) : Result<TodoResponse>.Success(Map(todo));
    }
    public async Task<Result<TodoResponse>> Create(Guid organizationId, Guid projectId, Guid userId, CreateTodoRequest request, CancellationToken ct)
    {
        if ((Validate(request) ?? await CheckAccess(organizationId, projectId, userId, true, ct)) is { } error) return Result<TodoResponse>.Failure(error);
        var todo = new Todo(projectId, request.Name, request.Description ?? "", request.TargetDate, clock.GetUtcNow());
        db.Add(todo);
        await db.SaveChangesAsync(ct);
        return Result<TodoResponse>.Success(Map(todo));
    }
    public async Task<Result<TodoTaskResponse>> AddTask(Guid organizationId, Guid projectId, Guid todoId, Guid userId, CreateTodoTaskRequest request, CancellationToken ct)
    {
        if ((Validate(request) ?? await CheckAccess(organizationId, projectId, userId, true, ct)) is { } error) return Result<TodoTaskResponse>.Failure(error);
        if (!await db.Set<Todo>().AnyAsync(t => t.Id == todoId && t.ProjectId == projectId, ct)) return Result<TodoTaskResponse>.Failure(Missing);
        var task = new TodoTask(todoId, request.Title, request.Description ?? "", request.Status, clock.GetUtcNow());
        db.Add(task);
        await db.SaveChangesAsync(ct);
        return Result<TodoTaskResponse>.Success(Map(task, projectId));
    }
    public async Task<Result<TodoTaskResponse>> MoveTask(Guid organizationId, Guid projectId, Guid todoId, Guid taskId, Guid userId, MoveTodoTaskRequest request, CancellationToken ct)
    {
        if ((Validate(request) ?? await CheckAccess(organizationId, projectId, userId, true, ct)) is { } error) return Result<TodoTaskResponse>.Failure(error);
        if (!await db.Set<Todo>().AnyAsync(t => t.Id == todoId && t.ProjectId == projectId, ct)) return Result<TodoTaskResponse>.Failure(Missing);
        var task = await db.Set<TodoTask>().SingleOrDefaultAsync(t => t.Id == taskId && t.TodoId == todoId, ct);
        if (task is null) return Result<TodoTaskResponse>.Failure(Missing);
        task.Move(request.Status, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return Result<TodoTaskResponse>.Success(Map(task, projectId));
    }
}
