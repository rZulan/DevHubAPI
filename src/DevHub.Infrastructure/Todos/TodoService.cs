using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Todos;
using DevHub.Domain.Organizations;
using DevHub.Domain.Todos;
using DevHub.Domain.Notifications;
using DevHub.Application.Notifications;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Todos;

internal sealed class TodoService(ApplicationDbContext db, IOrganizationRepository organizations, TimeProvider clock, IWorkshopActivity activity,
    DevHub.Application.Workshops.IWorkshopUsage usage, DevHub.Application.Workshops.WorkshopLimits limits) : ITodoService
{
    private Task<Error?> ReserveCreation(Guid userId, CancellationToken ct) => usage.Reserve(
        [new($"create-user:{userId:N}", limits.CreatesPerMinute, 1, TimeSpan.FromMinutes(1))], ct);
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
        void Dates(DateOnly? start, DateOnly? target)
        {
            if (!TodoSchedule.IsValid(start, target)) errors["TargetDate"] = ["Choose a target date on or after the From date."];
        }
        switch (request)
        {
            case CreateTodoRequest todo:
                Dates(todo.StartDate, todo.TargetDate);
                Text("Name", todo.Name, 150, true);
                Text("Description", todo.Description, 2000);
                break;
            case CreateTodoTaskRequest task:
                Dates(task.StartDate, task.TargetDate);
                Text("Title", task.Title, 150, true);
                Text("Description", task.Description, 2000);
                Status(task.Status);
                break;
            case MoveTodoTaskRequest move:
                Status(move.Status);
                break;
            case UpdateTodoTaskRequest edit:
                Dates(edit.StartDate, edit.TargetDate);
                Text("Title", edit.Title, 150, true);
                Text("Description", edit.Description, 2000);
                Status(edit.Status);
                if (edit.AssigneeIds is null) errors["AssigneeIds"] = ["Choose assignees or leave the list empty."];
                break;
            case UpdateTodoDatesRequest dates:
                Dates(dates.StartDate, dates.TargetDate);
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
    private static TodoTaskResponse Map(TodoTask task, Guid projectId) => new(task.Id, task.TodoId, projectId, task.Title, task.Description, task.Status, task.AssigneeId, task.Assignees.Select(a => a.UserId).Order().ToArray(), task.TargetDate, task.StartDate);
    private bool QueueAssignments(Guid organizationId, Guid projectId, Guid actorId, TodoTask task, IEnumerable<Guid> assignees)
    {
        var ids = assignees.ToHashSet();
        foreach (var userId in ids.Except(task.Assignees.Select(a => a.UserId)).Where(id => id != actorId))
            db.UserNotifications.Add(new UserNotification(organizationId, userId, actorId, UserNotification.TaskAssigned,
                task.Title, task.TodoId, task.Id, clock.GetUtcNow(), projectId));
        return !ids.SetEquals(task.Assignees.Select(a => a.UserId));
    }
    private static TodoResponse Map(Todo todo) => new(todo.Id, todo.ProjectId, todo.Name, todo.Description, todo.TargetDate,
        todo.CreatedAtUtc, todo.Tasks.OrderBy(t => t.CreatedAtUtc).ThenBy(t => t.Id).Select(t => Map(t, todo.ProjectId)).ToArray(), todo.Columns, todo.BoardRevision, todo.StartDate);
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
        if (await ReserveCreation(userId, ct) is { } quotaError) return Result<TodoResponse>.Failure(quotaError);
        var todo = new Todo(projectId, request.Name, request.Description ?? "", request.TargetDate, clock.GetUtcNow(), request.StartDate);
        db.Add(todo);
        await activity.Queue(new(organizationId, userId, "todo-created", todo.Name, todo.Id, todo.Id, projectId), ct);
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

    public async Task<Result<TodoResponse>> UpdateDates(Guid organizationId, Guid projectId, Guid todoId, Guid userId, UpdateTodoDatesRequest request, CancellationToken ct)
    {
        if ((Validate(request) ?? await CheckAccess(organizationId, projectId, userId, true, ct)) is { } error) return Result<TodoResponse>.Failure(error);
        var todo = await db.Set<Todo>().Include(t => t.Tasks).ThenInclude(t => t.Assignees).SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        if (todo is null) return Result<TodoResponse>.Failure(Missing);
        if (todo.BoardRevision != request.BoardRevision) return Result<TodoResponse>.Failure(BoardChanged);
        var changed = todo.StartDate != request.StartDate || todo.TargetDate != request.TargetDate;
        todo.SetDates(request.StartDate, request.TargetDate, clock.GetUtcNow());
        todo.TouchBoard(clock.GetUtcNow());
        if (changed) await activity.Queue(new(organizationId, userId, "todo-updated", todo.Name, todo.Id, todo.Id, projectId), ct);
        return await SaveBoard(ct) ? Result<TodoResponse>.Success(Map(todo)) : Result<TodoResponse>.Failure(BoardChanged);
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
        var previousColumns = todo.Columns;
        todo.ConfigureColumns(request.Columns, clock.GetUtcNow());
        if (!previousColumns.SequenceEqual(todo.Columns)) await activity.Queue(new(organizationId, userId, "todo-updated", todo.Name, todo.Id, todo.Id, projectId), ct);
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
        if (await ReserveCreation(userId, ct) is { } quotaError) return Result<TodoTaskResponse>.Failure(quotaError);
        var task = new TodoTask(todoId, request.Title, request.Description ?? "", request.Status, clock.GetUtcNow());
        task.SetDates(request.StartDate, request.TargetDate, clock.GetUtcNow());
        QueueAssignments(organizationId, projectId, userId, task, ids);
        task.AssignMany(ids, clock.GetUtcNow());
        todo.TouchBoard(clock.GetUtcNow());
        db.Add(task);
        await activity.Queue(new(organizationId, userId, "task-created", task.Title, todoId, task.Id, projectId), ct);
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
        var changed = QueueAssignments(organizationId, projectId, userId, task, request.AssigneeId is { } assignedId ? [assignedId] : []);
        task.Assign(request.AssigneeId, clock.GetUtcNow());
        if (changed) await activity.Queue(new(organizationId, userId, "task-updated", task.Title, todoId, task.Id, projectId), ct);
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
        var changed = QueueAssignments(organizationId, projectId, userId, task, ids);
        task.AssignMany(ids, clock.GetUtcNow());
        todo.TouchBoard(clock.GetUtcNow());
        if (changed) await activity.Queue(new(organizationId, userId, "task-updated", task.Title, todoId, task.Id, projectId), ct);
        return await SaveBoard(ct) ? Result<TodoTaskResponse>.Success(Map(task, projectId)) : Result<TodoTaskResponse>.Failure(BoardChanged);
    }

    public async Task<Result<TodoTaskResponse>> UpdateTask(Guid organizationId, Guid projectId, Guid todoId, Guid taskId, Guid userId, UpdateTodoTaskRequest request, CancellationToken ct)
    {
        if ((Validate(request) ?? await CheckAccess(organizationId, projectId, userId, true, ct)) is { } error) return Result<TodoTaskResponse>.Failure(error);
        var todo = await db.Set<Todo>().SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        if (todo is null) return Result<TodoTaskResponse>.Failure(Missing);
        var task = await db.Set<TodoTask>().Include(t => t.Assignees).SingleOrDefaultAsync(t => t.Id == taskId && t.TodoId == todoId, ct);
        if (task is null) return Result<TodoTaskResponse>.Failure(Missing);
        if (todo.BoardRevision != request.BoardRevision) return Result<TodoTaskResponse>.Failure(BoardChanged);
        if (!todo.Columns.Any(c => c.Id == request.Status)) return Result<TodoTaskResponse>.Failure(InvalidStatus);
        // Preserve existing assignments to former members; only new assignments need membership validation.
        foreach (var id in request.AssigneeIds.Distinct().Where(id => !task.Assignees.Any(a => a.UserId == id)))
            if (await ValidateAssignee(organizationId, id, ct) is { } assigneeError) return Result<TodoTaskResponse>.Failure(assigneeError);
        var now = clock.GetUtcNow();
        var changed = task.Title != request.Title.Trim() || task.Description != (request.Description ?? "").Trim() ||
            task.Status != request.Status || task.StartDate != request.StartDate || task.TargetDate != request.TargetDate || !task.Assignees.Select(a => a.UserId).ToHashSet().SetEquals(request.AssigneeIds);
        task.UpdateDetails(request.Title, request.Description ?? "", request.Status, now);
        task.SetDates(request.StartDate, request.TargetDate, now);
        QueueAssignments(organizationId, projectId, userId, task, request.AssigneeIds);
        task.AssignMany(request.AssigneeIds, now);
        if (changed) await activity.Queue(new(organizationId, userId, "task-updated", task.Title, todoId, task.Id, projectId), ct);
        todo.TouchBoard(now);
        return await SaveBoard(ct) ? Result<TodoTaskResponse>.Success(Map(task, projectId)) : Result<TodoTaskResponse>.Failure(BoardChanged);
    }

    public async Task<Result<bool>> DeleteTask(Guid organizationId, Guid projectId, Guid todoId, Guid taskId, Guid userId, DeleteTodoTaskRequest request, CancellationToken ct)
    {
        if (await CheckAccess(organizationId, projectId, userId, true, ct) is { } error) return Result<bool>.Failure(error);
        var todo = await db.Set<Todo>().SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        if (todo is null) return Result<bool>.Failure(Missing);
        var task = await db.Set<TodoTask>().SingleOrDefaultAsync(t => t.Id == taskId && t.TodoId == todoId, ct);
        if (task is null) return Result<bool>.Failure(Missing);
        if (todo.BoardRevision != request.BoardRevision) return Result<bool>.Failure(BoardChanged);
        db.Remove(task);
        await activity.Queue(new(organizationId, userId, "task-deleted", task.Title, todoId, task.Id, projectId), ct);
        todo.TouchBoard(clock.GetUtcNow());
        return await SaveBoard(ct) ? Result<bool>.Success(true) : Result<bool>.Failure(BoardChanged);
    }

    public async Task<Result<TodoTaskResponse>> MoveTask(Guid organizationId, Guid projectId, Guid todoId, Guid taskId, Guid userId, MoveTodoTaskRequest request, CancellationToken ct)
    {
        if ((Validate(request) ?? await CheckAccess(organizationId, projectId, userId, true, ct)) is { } error) return Result<TodoTaskResponse>.Failure(error);
        var todo = await db.Set<Todo>().SingleOrDefaultAsync(t => t.Id == todoId && t.ProjectId == projectId, ct);
        if (todo is null) return Result<TodoTaskResponse>.Failure(Missing);
        if (!todo.Columns.Any(c => c.Id == request.Status)) return Result<TodoTaskResponse>.Failure(InvalidStatus);
        var task = await db.Set<TodoTask>().Include(t => t.Assignees).SingleOrDefaultAsync(t => t.Id == taskId && t.TodoId == todoId, ct);
        if (task is null) return Result<TodoTaskResponse>.Failure(Missing);
        var changed = task.Status != request.Status;
        task.Move(request.Status, clock.GetUtcNow());
        if (changed) await activity.Queue(new(organizationId, userId, "task-updated", task.Title, todoId, task.Id, projectId), ct);
        todo.TouchBoard(clock.GetUtcNow());
        return await SaveBoard(ct) ? Result<TodoTaskResponse>.Success(Map(task, projectId)) : Result<TodoTaskResponse>.Failure(BoardChanged);
    }
}
