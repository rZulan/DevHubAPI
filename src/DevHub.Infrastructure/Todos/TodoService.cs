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
            Text("Status", value, 20, true);
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
    private async Task<Error?> ValidateAssignee(Guid organizationId, Guid? assigneeId, CancellationToken ct)
    {
        if (assigneeId is null) return null;
        var organization = await organizations.GetByIdAsync(organizationId, ct);
        return organization?.HasMember(assigneeId.Value) == true ? null :
            new Error("Todos.InvalidAssignee", "Choose a current organization member.", ErrorType.Validation);
    }
    private static TodoTaskResponse Map(TodoTask task, Guid projectId) => new(task.Id, task.TodoId, projectId, task.Title, task.Description, task.Status, task.AssigneeId, task.Assignees.Select(a => a.UserId).Order().ToArray());
    private static TodoResponse Map(Todo todo) => new(todo.Id, todo.ProjectId, todo.Name, todo.Description, todo.TargetDate,
        todo.CreatedAtUtc, todo.Tasks.OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id).Select(t => Map(t, todo.ProjectId)).ToArray(), todo.Columns, todo.BoardRevision);
    public async Task<Result<IReadOnlyList<TodoResponse>>> List(Guid organizationId, Guid projectId, Guid userId, CancellationToken ct)
    {
        if (await CheckAccess(organizationId, projectId, userId, false, ct) is { } error) return Result<IReadOnlyList<TodoResponse>>.Failure(error);
        var todos = await db.Set<Todo>().AsNoTracking().Include(t => t.Tasks).ThenInclude(t => t.Assignees).Where(t => t.ProjectId == projectId).OrderByDescending(t => t.CreatedAtUtc).ToListAsync(ct);
        return Result<IReadOnlyList<TodoResponse>>.Success(todos.Select(Map).ToArray());
    }
    public async Task<Result<TodoPageResponse>> ListPage(Guid organizationId, Guid projectId, Guid userId, int page, CancellationToken ct, int pageSize = 12)
    {
        if (await CheckAccess(organizationId, projectId, userId, false, ct) is { } error) return Result<TodoPageResponse>.Failure(error);
        if (pageSize < 1 || pageSize > 100 || page < 0 || page > int.MaxValue / pageSize)
            return Result<TodoPageResponse>.Failure(new Error("Todos.InvalidPage", "Choose a valid page.", ErrorType.Validation));
        var items = await db.Set<Todo>().AsNoTracking().Include(t => t.Tasks).ThenInclude(t => t.Assignees)
            .Where(t => t.ProjectId == projectId).OrderByDescending(t => t.CreatedAtUtc).ThenByDescending(t => t.Id)
            .Skip(page * pageSize).Take(pageSize + 1).ToListAsync(ct);
        return Result<TodoPageResponse>.Success(new(items.Take(pageSize).Select(Map).ToArray(), items.Count > pageSize));
    }
    public async Task<Result<TodoResponse>> Get(Guid organizationId, Guid projectId, Guid todoId, Guid userId, CancellationToken ct)
    {
        if (await CheckAccess(organizationId, projectId, userId, false, ct) is { } error) return Result<TodoResponse>.Failure(error);
        var todo = await db.Set<Todo>().AsNoTracking().Include(t => t.Tasks).ThenInclude(t => t.Assignees)
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
    private static readonly Error InvalidStatus = new("Todos.InvalidStatus", "Choose a column on this TODO board.", ErrorType.Validation);
    private static readonly Error BoardChanged = new("Todos.BoardChanged", "This board changed. Reload it and try again.", ErrorType.Conflict);

    private async Task<bool> SaveBoard(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (PersistenceConcurrencyException) { db.ChangeTracker.Clear(); return false; }
    }

    public async Task<Result<TodoResponse>> ConfigureColumns(Guid organizationId, Guid projectId, Guid todoId, Guid userId, ConfigureTodoColumnsRequest request, CancellationToken ct)
    {
        if (await CheckAccess(organizationId, projectId, userId, true, ct) is { } error) return Result<TodoResponse>.Failure(error);
        if (request.Columns is null || request.Columns.Count is < 1 or > 5 || request.Columns.Any(c => c is null || string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 20 || !System.Text.RegularExpressions.Regex.IsMatch(c.Id, "^[a-zA-Z0-9_-]+$") || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 50)
            || request.Columns.Select(c => c.Id).Distinct().Count() != request.Columns.Count)
            return Result<TodoResponse>.Failure(new Error("Todos.InvalidColumns", "Use one to five columns with unique IDs and names of 1–50 characters.", ErrorType.Validation));
        var todo = await db.Set<Todo>().Include(t => t.Tasks).ThenInclude(t => t.Assignees).SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        if (todo is null) return Result<TodoResponse>.Failure(Missing);
        if (todo.BoardRevision != request.BoardRevision) return Result<TodoResponse>.Failure(BoardChanged);
        todo.ConfigureColumns(request.Columns, clock.GetUtcNow());
        return await SaveBoard(ct) ? Result<TodoResponse>.Success(Map(todo)) : Result<TodoResponse>.Failure(BoardChanged);
    }

    public async Task<Result<TodoTaskResponse>> AddTask(Guid organizationId, Guid projectId, Guid todoId, Guid userId, CreateTodoTaskRequest request, CancellationToken ct)
    {
        if ((Validate(request) ?? await CheckAccess(organizationId, projectId, userId, true, ct)) is { } error) return Result<TodoTaskResponse>.Failure(error);
        var todo = await db.Set<Todo>().SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        if (todo is null) return Result<TodoTaskResponse>.Failure(Missing);
        if (!todo.Columns.Any(c => c.Id == request.Status)) return Result<TodoTaskResponse>.Failure(InvalidStatus);
        var ids = request.AssigneeIds ?? (request.AssigneeId is { } id ? new[] { id } : Array.Empty<Guid>());
        foreach (var assigneeId in ids.Distinct())
            if (await ValidateAssignee(organizationId, assigneeId, ct) is { } assigneeError) return Result<TodoTaskResponse>.Failure(assigneeError);
        var task = new TodoTask(todoId, request.Title, request.Description ?? "", request.Status, clock.GetUtcNow());
        task.AssignMany(ids, clock.GetUtcNow());
        todo.TouchBoard(clock.GetUtcNow());
        db.Add(task);
        return await SaveBoard(ct) ? Result<TodoTaskResponse>.Success(Map(task, projectId)) : Result<TodoTaskResponse>.Failure(BoardChanged);
    }

    public async Task<Result<TodoTaskResponse>> AssignTask(Guid organizationId, Guid projectId, Guid todoId, Guid taskId, Guid userId, AssignTodoTaskRequest request, CancellationToken ct)
    {
        if (await CheckAccess(organizationId, projectId, userId, true, ct) is { } error) return Result<TodoTaskResponse>.Failure(error);
        var todo = await db.Set<Todo>().SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        if (todo is null) return Result<TodoTaskResponse>.Failure(Missing);
        var task = await db.Set<TodoTask>().Include(t => t.Assignees).SingleOrDefaultAsync(t => t.Id == taskId && t.TodoId == todoId, ct);
        if (task is null) return Result<TodoTaskResponse>.Failure(Missing);
        if (await ValidateAssignee(organizationId, request.AssigneeId, ct) is { } assigneeError) return Result<TodoTaskResponse>.Failure(assigneeError);
        task.Assign(request.AssigneeId, clock.GetUtcNow());
        todo.TouchBoard(clock.GetUtcNow());
        return await SaveBoard(ct) ? Result<TodoTaskResponse>.Success(Map(task, projectId)) : Result<TodoTaskResponse>.Failure(BoardChanged);
    }

    public async Task<Result<TodoTaskResponse>> ChangeAssignee(Guid organizationId, Guid projectId, Guid todoId, Guid taskId, Guid userId, ChangeTodoTaskAssigneeRequest request, CancellationToken ct)
    {
        if (await CheckAccess(organizationId, projectId, userId, true, ct) is { } error) return Result<TodoTaskResponse>.Failure(error);
        var todo = await db.Set<Todo>().SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        if (todo is null) return Result<TodoTaskResponse>.Failure(Missing);
        var task = await db.Set<TodoTask>().Include(t => t.Assignees).SingleOrDefaultAsync(t => t.Id == taskId && t.TodoId == todoId, ct);
        if (task is null) return Result<TodoTaskResponse>.Failure(Missing);
        // Former members can be removed, but cannot be newly assigned.
        if (!request.Remove && await ValidateAssignee(organizationId, request.UserId, ct) is { } assigneeError) return Result<TodoTaskResponse>.Failure(assigneeError);
        var ids = task.Assignees.Select(a => a.UserId).ToHashSet();
        if (request.Remove) ids.Remove(request.UserId); else ids.Add(request.UserId);
        task.AssignMany(ids, clock.GetUtcNow());
        todo.TouchBoard(clock.GetUtcNow());
        return await SaveBoard(ct) ? Result<TodoTaskResponse>.Success(Map(task, projectId)) : Result<TodoTaskResponse>.Failure(BoardChanged);
    }

    public async Task<Result<TodoTaskResponse>> MoveTask(Guid organizationId, Guid projectId, Guid todoId, Guid taskId, Guid userId, MoveTodoTaskRequest request, CancellationToken ct)
    {
        if ((Validate(request) ?? await CheckAccess(organizationId, projectId, userId, true, ct)) is { } error) return Result<TodoTaskResponse>.Failure(error);
        var todo = await db.Set<Todo>().SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        if (todo is null) return Result<TodoTaskResponse>.Failure(Missing);
        if (!todo.Columns.Any(c => c.Id == request.Status)) return Result<TodoTaskResponse>.Failure(InvalidStatus);
        var task = await db.Set<TodoTask>().Include(t => t.Assignees).SingleOrDefaultAsync(t => t.Id == taskId && t.TodoId == todoId, ct);
        if (task is null) return Result<TodoTaskResponse>.Failure(Missing);
        task.Move(request.Status, clock.GetUtcNow());
        todo.TouchBoard(clock.GetUtcNow());
        return await SaveBoard(ct) ? Result<TodoTaskResponse>.Success(Map(task, projectId)) : Result<TodoTaskResponse>.Failure(BoardChanged);
    }
}
