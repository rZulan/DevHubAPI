using DevHub.Domain.Common;

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
    public ICollection<TodoTask> Tasks { get; private set; } = new List<TodoTask>();
}

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
    public void Move(string status, DateTimeOffset now) { Status = status; MarkUpdated(now); }
}
