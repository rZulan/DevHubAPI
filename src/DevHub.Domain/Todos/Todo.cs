using DevHub.Domain.Common;
using System.Text.Json;

namespace DevHub.Domain.Todos;

public sealed class Todo : BaseEntity
{
    private Todo() { }
    public Todo(Guid projectId, string name, string description, DateOnly? targetDate, DateTimeOffset now)
        : base(Guid.NewGuid(), now)
    {
        ProjectId = projectId;
        Name = name.Trim();
        Description = description.Trim();
        TargetDate = targetDate;
    }
    public Guid ProjectId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public DateOnly? TargetDate { get; private set; }
    public const string DefaultColumnsJson = "[{\"Id\":\"pending\",\"Name\":\"Pending\"},{\"Id\":\"progress\",\"Name\":\"In progress\"},{\"Id\":\"done\",\"Name\":\"Done\"}]";
    public string ColumnsJson { get; private set; } = DefaultColumnsJson;
    public int BoardRevision { get; private set; }
    public IReadOnlyList<TodoColumn> Columns => JsonSerializer.Deserialize<TodoColumn[]>(ColumnsJson)!;
    public ICollection<TodoTask> Tasks { get; private set; } = new List<TodoTask>();
    public void TouchBoard(DateTimeOffset now) { BoardRevision++; MarkUpdated(now); }
    public void ConfigureColumns(IReadOnlyList<TodoColumn> columns, DateTimeOffset now)
    {
        if (columns.Count is < 1 or > 5 || columns.Any(c => string.IsNullOrWhiteSpace(c.Id) || c.Id.Length > 20 || string.IsNullOrWhiteSpace(c.Name) || c.Name.Length > 50)
            || columns.Select(c => c.Id).Distinct().Count() != columns.Count)
            throw new ArgumentException("Use one to five columns with unique IDs and names of at most 50 characters.", nameof(columns));
        var previous = Columns;
        var remaining = columns.Select(c => c.Id).ToHashSet();
        foreach (var task in Tasks.Where(t => !remaining.Contains(t.Status)))
        {
            // Prefer the nearest surviving old column; ties move left.
            var oldIndex = previous.ToList().FindIndex(c => c.Id == task.Status);
            var destination = previous.Select((c, i) => new { Column = c, Index = i })
                .Where(c => remaining.Contains(c.Column.Id)).OrderBy(c => Math.Abs(c.Index - oldIndex))
                .Select(c => c.Column.Id).FirstOrDefault() ?? columns[0].Id;
            task.Move(destination, now);
        }
        ColumnsJson = JsonSerializer.Serialize(columns.Select(c => c with { Name = c.Name.Trim() }));
        TouchBoard(now);
    }
}

public sealed record TodoColumn(string Id, string Name);

public sealed class TodoTask : BaseEntity
{
    private TodoTask() { }
    public TodoTask(Guid todoId, string title, string description, string status, DateTimeOffset now)
        : base(Guid.NewGuid(), now)
    {
        TodoId = todoId;
        Title = title.Trim();
        Description = description.Trim();
        Status = status;
    }
    public Guid TodoId { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Status { get; private set; } = "pending";
    public Guid? AssigneeId { get; private set; }
    public ICollection<TodoTaskAssignee> Assignees { get; private set; } = new List<TodoTaskAssignee>();
    public void Assign(Guid? assigneeId, DateTimeOffset now) => AssignMany(assigneeId is { } id ? [id] : [], now);
    public void AssignMany(IEnumerable<Guid> assigneeIds, DateTimeOffset now)
    {
        var ids = assigneeIds.Distinct().ToHashSet();
        foreach (var assignment in Assignees.Where(a => !ids.Contains(a.UserId)).ToArray()) Assignees.Remove(assignment);
        foreach (var id in ids.Where(id => !Assignees.Any(a => a.UserId == id))) Assignees.Add(new TodoTaskAssignee(Id, id));
        AssigneeId = ids.Select(id => (Guid?)id).FirstOrDefault();
        MarkUpdated(now);
    }
    public void Move(string status, DateTimeOffset now) { Status = status; MarkUpdated(now); }
}

public sealed class TodoTaskAssignee
{
    private TodoTaskAssignee() { }
    public TodoTaskAssignee(Guid taskId, Guid userId) { TaskId = taskId; UserId = userId; }
    public Guid TaskId { get; private set; }
    public Guid UserId { get; private set; }
}
