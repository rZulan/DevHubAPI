using DevHub.Application.Connections;
using DevHub.Domain.Todos;

namespace DevHub.Infrastructure.Todos;

internal sealed class TodoConnectionEvents(IConnectionEvents events)
{
    private static readonly ConnectionNodeRef TodoTool = new("todo");
    private static ConnectionNodeRef BoardNode(Guid id) => new("todo-board", id.ToString());
    private static ConnectionNodeRef TaskNode(Guid boardId, Guid taskId) => new("todo-task", taskId.ToString(), boardId.ToString());

    public Task Board(Guid orgId, Guid actor, Todo todo, string kind, CancellationToken ct) => events.Enqueue(orgId,
        todo.ProjectId, actor, new(BoardNode(todo.Id), kind, new Dictionary<string, string> { ["name"] = todo.Name }, [TodoTool]), ct);

    public Task BoardCompletion(Guid orgId, Guid actor, Todo todo, bool wasFinished, CancellationToken ct) =>
        wasFinished == todo.IsFinished() ? Task.CompletedTask : Board(orgId, actor, todo, wasFinished ? "board-reopened" : "board-completed", ct);

    public Task Column(Guid orgId, Guid actor, Todo todo, TodoColumn column, string kind, CancellationToken ct) => events.Enqueue(orgId,
        todo.ProjectId, actor, new(new("todo-column", column.Id, todo.Id.ToString()), kind,
            new Dictionary<string, string> { ["name"] = column.Name }, [BoardNode(todo.Id), TodoTool]), ct);

    public Task TaskEvent(Guid orgId, Guid actor, Todo todo, TodoTask task, string kind, CancellationToken ct,
        string? previous = null, string? person = null)
    {
        var fields = new Dictionary<string, string> { ["title"] = task.Title, ["status"] = task.Status };
        if (previous is not null) fields["previousStatus"] = previous;
        if (person is not null) fields["userId"] = person;
        return events.Enqueue(orgId, todo.ProjectId, actor, new(TaskNode(todo.Id, task.Id), kind, fields, [BoardNode(todo.Id), TodoTool]), ct);
    }

    public async Task TaskTransition(Guid orgId, Guid actor, Todo todo, TodoTask task, string previousStatus, bool wasFinished, CancellationToken ct)
    {
        if (previousStatus != task.Status)
        {
            await TaskEvent(orgId, actor, todo, task, "status-changed", ct, previousStatus);
            var fields = new Dictionary<string, string> { ["title"] = task.Title, ["status"] = task.Status, ["previousStatus"] = previousStatus };
            await events.Enqueue(orgId, todo.ProjectId, actor, new(new("todo-column", previousStatus, todo.Id.ToString()), "task-exited", fields, [BoardNode(todo.Id), TodoTool]), ct);
            await events.Enqueue(orgId, todo.ProjectId, actor, new(new("todo-column", task.Status, todo.Id.ToString()), "task-entered", fields, [BoardNode(todo.Id), TodoTool]), ct);
        }
        if (wasFinished != (task.Status == todo.Columns[^1].Id))
            await TaskEvent(orgId, actor, todo, task, wasFinished ? "task-reopened" : "task-completed", ct);
    }
}
