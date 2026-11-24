using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevHub.Domain.Todos;
using DevHub.Infrastructure.Connections;
using DevHub.Infrastructure.Persistence;
using DevHub.Infrastructure.Todos;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.ProjectTools;

internal sealed class SdlcTodoSync(ApplicationDbContext db, TodoConnectionEvents events, TimeProvider clock)
{
    internal async Task<bool> Sync(Guid organizationId, Guid projectId, Guid userId, JsonElement previous, JsonElement next, CancellationToken ct)
    {
        if (RoadmapId(next) is not { } roadmapId) return false;
        var previousPhases = RoadmapId(previous) == roadmapId
            ? SdlcConnections.TodoPhases(previous).ToDictionary(phase => phase.Key)
            : [];
        var phases = SdlcConnections.TodoPhases(next).Where(phase =>
            !previousPhases.TryGetValue(phase.Key, out var prior) ||
            prior.TargetDate != phase.TargetDate || !prior.Deliverables.SequenceEqual(phase.Deliverables)).ToArray();
        if (phases.Length == 0) return false;

        var boardIds = phases.Select(phase => LinkedId(roadmapId, "phase", phase.Key)).ToArray();
        var boards = await db.Set<Todo>().Include(board => board.Tasks)
            .Where(board => board.ProjectId == projectId && boardIds.Contains(board.Id))
            .ToDictionaryAsync(board => board.Id, ct);
        var now = clock.GetUtcNow();
        var saved = false;
        foreach (var phase in phases)
        {
            var id = LinkedId(roadmapId, "phase", phase.Key);
            var isNew = !boards.TryGetValue(id, out var board);
            if (isNew)
            {
                board = new Todo(projectId, phase.Name, phase.Description, phase.TargetDate, now, id: id);
                db.Add(board);
                await events.Board(organizationId, userId, board, "board-created", ct);
            }

            var prior = previousPhases.GetValueOrDefault(phase.Key);
            saved |= await SyncPhase(organizationId, userId, board!, phase, isNew ? null : prior, isNew, now, ct);
        }
        return saved;
    }

    private async Task<bool> SyncPhase(Guid organizationId, Guid userId, Todo board, SdlcConnections.TodoPhase phase,
        SdlcConnections.TodoPhase? prior, bool isNew, DateTimeOffset now, CancellationToken ct)
    {
        var changed = isNew;
        var wasFinished = board.IsFinished();
        if (prior?.TargetDate != phase.TargetDate && board.TargetDate != phase.TargetDate)
        {
            board.SetDates(CompatibleStart(board.StartDate, phase.TargetDate), phase.TargetDate, now);
            await events.Board(organizationId, userId, board, "board-dates-changed", ct);
            changed = true;
        }

        var columns = board.Columns;
        var first = columns[0].Id;
        var final = columns[^1].Id;
        var tasks = board.Tasks.ToDictionary(task => task.Id);
        var previousItems = prior?.Deliverables.ToDictionary(item => item.Name, StringComparer.Ordinal) ?? [];
        foreach (var deliverable in phase.Deliverables)
        {
            var id = LinkedId(board.Id, "deliverable", deliverable.Name);
            var status = deliverable.Checked ? final : first;
            if (!tasks.TryGetValue(id, out var task))
            {
                var label = deliverable.Name.Trim();
                var name = ShortTitle(label);
                task = new TodoTask(board.Id, name, label.Length > name.Length ? label : "", status, now, id);
                task.SetDates(null, deliverable.TargetDate, now);
                task.RecordCompletion(false, final, now);
                if (isNew) task.SetSortOrder(board.Tasks.Count);
                board.Tasks.Add(task);
                db.Add(task);
                await events.TaskEvent(organizationId, userId, board, task, "task-created", ct);
                changed = true;
                continue;
            }

            var previous = previousItems.GetValueOrDefault(deliverable.Name);
            if (previous?.TargetDate != deliverable.TargetDate && task.TargetDate != deliverable.TargetDate)
            {
                task.SetDates(CompatibleStart(task.StartDate, deliverable.TargetDate), deliverable.TargetDate, now);
                await events.TaskEvent(organizationId, userId, board, task, "task-updated", ct);
                changed = true;
            }
            if (previous?.Checked != deliverable.Checked && task.Status != status)
            {
                var previousStatus = task.Status;
                task.Move(status, now);
                task.RecordCompletion(previousStatus == final, final, now);
                await events.TaskTransition(organizationId, userId, board, task, previousStatus, previousStatus == final, ct);
                changed = true;
            }
        }

        // A removed deliverable no longer participates in the sync. Keep its To-do task and any manual work on it.
        if (!changed) return false;
        board.RecordCompletion(wasFinished, now);
        board.TouchBoard(now);
        await events.BoardCompletion(organizationId, userId, board, wasFinished, ct);
        return true;
    }

    private static Guid? RoadmapId(JsonElement document) =>
        document.ValueKind == JsonValueKind.Object && document.TryGetProperty("connectToTodo", out var enabled) && enabled.ValueKind == JsonValueKind.True &&
        document.TryGetProperty("roadmapId", out var id) && id.ValueKind == JsonValueKind.String &&
        id.TryGetGuid(out var roadmapId) ? roadmapId : null;

    private static Guid LinkedId(Guid parent, string kind, string key) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"devhub:sdlc:{kind}/{parent:N}/{key}")).AsSpan(0, 16));

    private static DateOnly? CompatibleStart(DateOnly? start, DateOnly? target) => start <= target ? start : null;

    private static string ShortTitle(string title) =>
        title.Length <= 150 ? title : title[..(char.IsHighSurrogate(title[149]) ? 149 : 150)];
}
