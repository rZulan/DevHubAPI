using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.BugReports;
using DevHub.Application.Common;
using DevHub.Application.Connections;
using DevHub.Application.Ideas;
using DevHub.Application.ProjectTools;
using DevHub.Application.Todos;
using DevHub.Domain.Organizations;
using DevHub.Domain.Todos;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Connections;

internal sealed class ConnectionTools(ApplicationDbContext db, IIdeasService access, IOrganizationRepository organizations,
    ITodoService todos, IBugReportsService bugs, IProjectToolsService documents, TimeProvider clock) : IConnectionTools
{
    public IReadOnlyList<ConnectionCapability> Catalog => ConnectionCatalog.Entries;
    private static readonly Error Missing = new("Connections.NodeUnavailable", "The selected item is unavailable.", ErrorType.NotFound);
    private static readonly Error Forbidden = new("Connections.ToolPermission", "The rule owner needs permission to edit the target tool.", ErrorType.Forbidden);
    private static Error Invalid(string message) => new("Connections.InvalidArguments", message, ErrorType.Validation);
    private static string? Tool(string kind) => ConnectionCatalog.Entries.FirstOrDefault(entry => entry.NodeKind == kind)?.Tool;

    private Task<bool> CanView(ConnectionScope scope, CancellationToken ct) => access.CanView(scope.OrganizationId, scope.ProjectId, scope.UserId, ct);

    public async Task<bool> CanManage(ConnectionScope scope, string targetKind, CancellationToken ct)
    {
        var permission = Tool(targetKind) switch
        {
            "todo" or "calendar" => OrganizationPermissions.ManageTasks,
            "erd" => OrganizationPermissions.EditErd,
            "bug-reports" => OrganizationPermissions.ManageBugReports,
            "ui-editor" => OrganizationPermissions.EditUiDesigns,
            "uml" => OrganizationPermissions.EditUmlDiagrams,
            "sdlc" => OrganizationPermissions.ManageRoadmap,
            "wiki" => OrganizationPermissions.ManageWiki,
            "notes" => OrganizationPermissions.ManageNotes,
            _ => null
        };
        return permission is not null && await CanView(scope, ct) &&
            (await organizations.GetByIdAsync(scope.OrganizationId, ct))?.HasPermission(scope.UserId, permission) == true;
    }

    public async Task<IReadOnlyList<ConnectionNode>> FindNodes(ConnectionScope scope, string? query, CancellationToken ct)
    {
        if (!await CanView(scope, ct)) return [];
        var nodes = ConnectionDocuments.Tools.Select(pair => new ConnectionNode(new(pair.Key), pair.Value, pair.Value)).ToList();
        var boardList = await todos.List(scope.OrganizationId, scope.ProjectId, scope.UserId, ct);
        foreach (var board in boardList.Value ?? []) nodes.AddRange(TodoNodes(board));

        var projectDocuments = await db.ProjectDocuments.AsNoTracking().Where(document => document.ProjectId == scope.ProjectId)
            .Select(document => new { document.ToolType, document.ContentJson }).ToArrayAsync(ct);
        foreach (var document in projectDocuments.Where(document => ConnectionDocuments.Tools.ContainsKey(document.ToolType)))
        {
            if (document.ContentJson is not { } content) continue;
            using var json = JsonDocument.Parse(content);
            nodes.AddRange(ConnectionDocuments.Snapshots(document.ToolType, json.RootElement).Select(node => new ConnectionNode(node.Reference, node.Label, node.Path)));
        }

        var reportItems = await db.BugReports.AsNoTracking().Where(report => report.ProjectId == scope.ProjectId)
            .Select(report => new { report.Id, report.Reference, report.Title }).ToArrayAsync(ct);
        nodes.AddRange(reportItems.Select(report => new ConnectionNode(new("bug-report", report.Id.ToString()),
            $"{report.Reference} {report.Title}", $"Bug Reports › {report.Reference} {report.Title}")));
        var categoryItems = await db.BugCategories.AsNoTracking().Where(category => category.ProjectId == scope.ProjectId)
            .Select(category => new { category.Id, category.Name }).ToArrayAsync(ct);
        nodes.AddRange(categoryItems.Select(category => new ConnectionNode(new("bug-category", category.Id.ToString()), category.Name, $"Bug Reports › Categories › {category.Name}")));
        query = query?.Trim();
        return nodes.Where(node => string.IsNullOrEmpty(query) || node.Path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            node.Reference.Id?.Equals(query, StringComparison.OrdinalIgnoreCase) == true).OrderBy(node => node.Reference.Id is not null)
            .ThenBy(node => node.Path, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Reference.Id, StringComparer.Ordinal).ToArray();
    }

    public async Task<ConnectionNode?> ResolveNode(ConnectionScope scope, ConnectionNodeRef reference, CancellationToken ct)
    {
        var tool = Tool(reference.Kind);
        if (tool is null || !await CanView(scope, ct)) return null;
        if (reference.Kind == tool && reference.Id is null && reference.ParentId is null)
        {
            var rootLabel = ConnectionDocuments.Tools[tool];
            Dictionary<string, IReadOnlyList<ConnectionOption>>? options = null;
            if (tool == "erd") options = await ErdOptions(scope, ct);
            if (tool == "wiki") options = await WikiOptions(scope, ct);
            if (tool == "bug-reports") options = new() { ["categoryId"] = await CategoryOptions(scope.ProjectId, ct) };
            return new(reference, rootLabel, rootLabel, options);
        }
        if (tool == "todo")
        {
            var board = await FindBoard(scope, reference, ct);
            var node = board is null ? null : TodoNodes(board).FirstOrDefault(node => node.Reference == reference);
            if (node is null) return null;
            var options = new Dictionary<string, IReadOnlyList<ConnectionOption>>(node.Options!);
            if (reference.Kind is "todo-task" or "todo-board") options["userId"] = await UserOptions(scope.OrganizationId, ct);
            if (reference.Kind == "todo-column") options["taskId"] = board!.Tasks.Select(task => new ConnectionOption(task.Id.ToString(), task.Title)).ToArray();
            return node with { Options = options };
        }
        if (tool == "bug-reports" && Guid.TryParse(reference.Id, out var itemId) && reference.ParentId is null)
        {
            if (reference.Kind == "bug-report")
            {
                var report = await db.BugReports.AsNoTracking().Where(item => item.ProjectId == scope.ProjectId && item.Id == itemId)
                    .Select(item => new { item.Reference, item.Title }).SingleOrDefaultAsync(ct);
                return report is null ? null : new(reference, $"{report.Reference} {report.Title}", $"Bug Reports › {report.Reference} {report.Title}",
                    new Dictionary<string, IReadOnlyList<ConnectionOption>> { ["categoryId"] = await CategoryOptions(scope.ProjectId, ct) });
            }
            if (reference.Kind == "bug-category")
            {
                var name = await db.BugCategories.AsNoTracking().Where(item => item.ProjectId == scope.ProjectId && item.Id == itemId)
                    .Select(item => item.Name).SingleOrDefaultAsync(ct);
                return name is null ? null : new(reference, name, $"Bug Reports › Categories › {name}");
            }
            return null;
        }
        var document = await LoadDocument(scope, tool, ct);
        var item = ConnectionDocuments.Snapshots(tool, document.Document ?? default).FirstOrDefault(node => node.Reference == reference);
        if (item is null) return null;
        Dictionary<string, IReadOnlyList<ConnectionOption>>? itemOptions = null;
        if (tool == "wiki") itemOptions = await WikiOptions(scope, ct, item.Reference.Id);
        if (reference.Kind is "erd-table" or "erd-relationship")
        {
            itemOptions = await ErdOptions(scope, ct);
            if (reference.Kind == "erd-table")
            {
                itemOptions["fromField"] = itemOptions.GetValueOrDefault($"field:{item.Reference.Id}") ?? [];
                itemOptions["constraintId"] = ConstraintOptions(item, "indexes");
                itemOptions["checkId"] = ConstraintOptions(item, "checks");
            }
        }
        if (tool == "sdlc" && reference.Kind == "sdlc-phase") itemOptions = new() { ["ownerId"] = await UserOptions(scope.OrganizationId, ct) };
        if (reference.Kind == "ui-layer")
        {
            var screens = ConnectionDocuments.Snapshots(tool, document.Document ?? default).Where(node => node.Reference.Kind == "ui-screen")
                .Select(node => new ConnectionOption(node.Reference.Id!, node.Label)).ToArray();
            itemOptions = new() { ["parentId"] = screens, ["targetId"] = screens };
        }
        if (reference.Kind is "uml-diagram" or "uml-relationship")
        {
            var diagramId = reference.Kind == "uml-diagram" ? reference.Id : reference.ParentId;
            var elements = ConnectionDocuments.Snapshots(tool, document.Document ?? default).Where(node =>
                node.Reference.Kind == "uml-element" && node.Reference.ParentId == diagramId).Select(node => new ConnectionOption(node.Reference.Id!, node.Label)).ToArray();
            itemOptions = new() { ["startId"] = elements, ["endId"] = elements };
        }
        return new(item.Reference, item.Label, item.Path, itemOptions);
    }

    private static IEnumerable<ConnectionNode> TodoNodes(TodoResponse board)
    {
        var boardId = board.Id.ToString();
        var path = $"TODOs › {board.Name}";
        var statuses = board.Columns.Select(column => new ConnectionOption(column.Id, column.Name)).ToArray();
        var options = new Dictionary<string, IReadOnlyList<ConnectionOption>> { ["status"] = statuses, ["previousStatus"] = statuses };
        yield return new(new("todo-board", boardId), board.Name, path, options);
        foreach (var column in board.Columns)
            yield return new(new("todo-column", column.Id, boardId), column.Name, $"{path} › Status columns › {column.Name}", options);
        foreach (var task in board.Tasks)
            yield return new(new("todo-task", task.Id.ToString(), boardId), task.Title, $"{path} › {task.Title}", options);
    }

    private async Task<TodoResponse?> FindBoard(ConnectionScope scope, ConnectionNodeRef node, CancellationToken ct)
    {
        var id = node.Kind == "todo-board" ? node.Id : node.ParentId;
        return Guid.TryParse(id, out var boardId) ? (await todos.Get(scope.OrganizationId, scope.ProjectId, boardId, scope.UserId, ct)).Value : null;
    }

    private async Task<IReadOnlyList<ConnectionOption>> UserOptions(Guid organizationId, CancellationToken ct)
    {
        var members = await db.Set<OrganizationMember>().AsNoTracking().Where(member => member.OrganizationId == organizationId)
            .Select(member => new { member.UserId, member.Nickname, member.User.FirstName, member.User.LastName, member.User.Username }).ToArrayAsync(ct);
        return members.Select(member => new ConnectionOption(member.UserId.ToString(), member.Nickname ??
                ($"{member.FirstName} {member.LastName}".Trim() is { Length: > 0 } name ? name : member.Username)))
            .OrderBy(option => option.Label, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task<IReadOnlyList<ConnectionOption>> CategoryOptions(Guid projectId, CancellationToken ct)
    {
        var categories = await db.BugCategories.AsNoTracking().Where(category => category.ProjectId == projectId && !category.Archived)
            .OrderBy(category => category.Name).Select(category => new { category.Id, category.Name }).ToArrayAsync(ct);
        return categories.Select(category => new ConnectionOption(category.Id.ToString(), category.Name)).ToArray();
    }

    private async Task<Dictionary<string, IReadOnlyList<ConnectionOption>>> WikiOptions(ConnectionScope scope, CancellationToken ct, string? exceptId = null)
    {
        var document = await LoadDocument(scope, "wiki", ct);
        var candidates = ConnectionDocuments.Snapshots("wiki", document.Document ?? default).Where(node =>
            node.Reference.Id != exceptId && (exceptId is null || node.Ancestors.All(ancestor => ancestor.Id != exceptId)));
        return new() { ["parentId"] = candidates.Select(node => new ConnectionOption(node.Reference.Id!, node.Path)).ToArray() };
    }

    private async Task<Dictionary<string, IReadOnlyList<ConnectionOption>>> ErdOptions(ConnectionScope scope, CancellationToken ct)
    {
        var document = await LoadDocument(scope, "erd", ct);
        var nodes = ConnectionDocuments.Snapshots("erd", document.Document ?? default).ToArray();
        var tables = nodes.Where(node => node.Reference.Kind == "erd-table").Select(node => new ConnectionOption(node.Reference.Id!, node.Label)).ToArray();
        var relations = nodes.Where(node => node.Reference.Kind == "erd-relationship").Select(node => new ConnectionOption(node.Reference.Id!, node.Label)).ToArray();
        var result = new Dictionary<string, IReadOnlyList<ConnectionOption>> { ["tableId"] = tables, ["fromTable"] = tables, ["toTable"] = tables, ["relationshipId"] = relations };
        foreach (var group in nodes.Where(node => node.Reference.Kind == "erd-column").GroupBy(node => node.Reference.ParentId))
            result[$"field:{group.Key}"] = group.Select(node => new ConnectionOption(node.Reference.Id!, node.Label)).ToArray();
        return result;
    }

    private static IReadOnlyList<ConnectionOption> ConstraintOptions(ConnectionSnapshot item, string key) =>
        item.Tracked.TryGetValue(key, out var values) && values.ValueKind == JsonValueKind.Array ? values.EnumerateArray()
            .Select(value => new ConnectionOption(ConnectionDocuments.Text(value, "id")!, ConnectionDocuments.Text(value, "name")!)).ToArray() : [];

    private async Task<ToolDocumentResponse> LoadDocument(ConnectionScope scope, string tool, CancellationToken ct)
    {
        var result = await documents.Get(scope.OrganizationId, scope.ProjectId, scope.UserId, tool, ct);
        return result.Value ?? new(0, null);
    }

    private static bool Same(string? actual, string? expected) => string.Equals(actual?.Trim(), expected?.Trim(), StringComparison.OrdinalIgnoreCase);

    public async Task<Result<bool>> Check(ConnectionScope scope, ConnectionCondition condition, CancellationToken ct)
    {
        var node = condition.Node;
        var tool = Tool(node.Kind);
        if (tool is null || !await CanView(scope, ct)) return Result<bool>.Failure(Missing);
        if (condition.Kind == "exists") return Result<bool>.Success(await ResolveNode(scope, node, ct) is not null);
        var args = condition.Arguments;
        if (tool == "todo") return await CheckTodo(scope, condition, ct);
        if (tool == "bug-reports") return await CheckBug(scope, condition, ct);
        var document = (await LoadDocument(scope, tool, ct)).Document ?? default;
        var values = ConnectionDocuments.Snapshots(tool, document).ToArray();
        var current = values.FirstOrDefault(value => value.Reference == node);
        if (node.Kind != tool && current is null) return Result<bool>.Failure(Missing);
        if (tool == "sdlc" && SdlcConnections.Check(values, condition) is { } sdlcResult) return Result<bool>.Success(sdlcResult);

        bool Has(string kind, string label, string? parentId = null) => values.Any(value => value.Reference.Kind == kind &&
            (parentId is null || value.Reference.ParentId == parentId) && Same(value.Label, label));
        string? Field(string key) => current?.Fields.GetValueOrDefault(key);
        var result = condition.Kind switch
        {
            "name-is" => Same(current?.Label, args.GetValueOrDefault("name")),
            "title-is" => Same(current?.Label, args.GetValueOrDefault("title")),
            "table-exists" => Has("erd-table", args.GetValueOrDefault("name", "")),
            "table-missing" => !Has("erd-table", args.GetValueOrDefault("name", "")),
            "column-exists" => Has("erd-column", args.GetValueOrDefault("name", ""), node.Id),
            "has-primary-key" => values.Any(value => value.Reference.Kind == "erd-column" && value.Reference.ParentId == node.Id && value.Fields.GetValueOrDefault("primary") == "true"),
            "index-exists" => current is not null && ConstraintOptions(current, "indexes").Any(option => Same(option.Label, args.GetValueOrDefault("name"))),
            "check-exists" => current is not null && ConstraintOptions(current, "checks").Any(option => Same(option.Label, args.GetValueOrDefault("name"))),
            "entry-exists" => Has("calendar-entry", args.GetValueOrDefault("title", "")),
            "note-exists" => Has("sticky-note", args.GetValueOrDefault("title", "")),
            "screen-exists" => Has("ui-screen", args.GetValueOrDefault("name", "")),
            "diagram-exists" => Has("uml-diagram", args.GetValueOrDefault("name", "")),
            "page-exists" => Has("wiki-page", args.GetValueOrDefault("title", "")),
            "type-is" => Same(Field("type"), args.GetValueOrDefault("type")),
            "cardinality-is" => Same(Field("cardinality"), args.GetValueOrDefault("cardinality")),
            "is-primary" => Field("primary") == "true",
            "all-day" => Field("allDay") == "true",
            "starts-on" => Field("startDate") == args.GetValueOrDefault("date"),
            "kind-is" => Same(Field("kind"), args.GetValueOrDefault("kind")),
            "diagram-type-is" => Same(Field("type"), args.GetValueOrDefault("type", "null")),
            "color-is" => Same(Field("color"), args.GetValueOrDefault("color")),
            "hidden" => Field("hidden") == "true",
            "locked" => Field("locked") == "true",
            "is-home" => node.Id == ConnectionDocuments.Text(document, "homePageId"),
            "is-start" => node.Id == ConnectionDocuments.Text(document, "startFrameId"),
            "body-contains" => current?.Tracked.TryGetValue("body", out var body) == true &&
                body.GetString()?.Contains(args.GetValueOrDefault("text", ""), StringComparison.OrdinalIgnoreCase) == true,
            _ => false
        };
        return Result<bool>.Success(result);
    }

    private async Task<Result<bool>> CheckTodo(ConnectionScope scope, ConnectionCondition condition, CancellationToken ct)
    {
        var args = condition.Arguments;
        if (condition.Kind == "board-exists")
        {
            var boards = await todos.List(scope.OrganizationId, scope.ProjectId, scope.UserId, ct);
            return boards.IsSuccess ? Result<bool>.Success(boards.Value!.Any(board => Same(board.Name, args.GetValueOrDefault("name")))) : Result<bool>.Failure(boards.Error!);
        }
        var node = condition.Node;
        var board = await FindBoard(scope, node, ct);
        if (board is null) return Result<bool>.Failure(Missing);
        if (node.Kind == "todo-board") return Result<bool>.Success(condition.Kind switch
        {
            "name-is" => Same(board.Name, args.GetValueOrDefault("name")),
            "completed" => board.Tasks.Count > 0 && board.Tasks.All(task => task.Status == board.Columns[^1].Id),
            "column-exists" => board.Columns.Any(column => column.Id == args.GetValueOrDefault("status")),
            _ => false
        });
        if (node.Kind == "todo-column")
        {
            var column = board.Columns.FirstOrDefault(column => column.Id == node.Id);
            return column is null ? Result<bool>.Failure(Missing) : Result<bool>.Success(condition.Kind == "final-column" ? column.Id == board.Columns[^1].Id : Same(column.Name, args.GetValueOrDefault("name")));
        }
        var target = board.Tasks.FirstOrDefault(task => task.Id.ToString() == node.Id);
        if (target is null) return Result<bool>.Failure(Missing);
        return Result<bool>.Success(condition.Kind switch
        {
            "title-is" => Same(target.Title, args.GetValueOrDefault("title")),
            "status-is" => target.Status == args.GetValueOrDefault("status"),
            "assigned-to" => Guid.TryParse(args.GetValueOrDefault("userId"), out var userId) && target.AssigneeIds.Contains(userId),
            "completed" => target.Status == board.Columns[^1].Id,
            _ => false
        });
    }

    private async Task<Result<bool>> CheckBug(ConnectionScope scope, ConnectionCondition condition, CancellationToken ct)
    {
        if (!Guid.TryParse(condition.Node.Id, out var id)) return Result<bool>.Failure(Missing);
        var args = condition.Arguments;
        if (condition.Node.Kind == "bug-category")
        {
            var item = await db.BugCategories.AsNoTracking().SingleOrDefaultAsync(item => item.ProjectId == scope.ProjectId && item.Id == id, ct);
            return item is null ? Result<bool>.Failure(Missing) : Result<bool>.Success(condition.Kind == "archived" ? item.Archived : Same(item.Name, args.GetValueOrDefault("name")));
        }
        var report = await bugs.Get(new(scope.OrganizationId, scope.ProjectId, scope.UserId), id, ct);
        if (!report.IsSuccess) return Result<bool>.Failure(report.Error!);
        var value = report.Value!;
        return Result<bool>.Success(condition.Kind switch
        {
            "status-is" => Same(value.Status, args.GetValueOrDefault("status")),
            "priority-is" => Same(value.Priority, args.GetValueOrDefault("priority")),
            "category-is" => string.IsNullOrWhiteSpace(args.GetValueOrDefault("categoryId")) ? value.CategoryId is null :
                Guid.TryParse(args.GetValueOrDefault("categoryId"), out var categoryId) && value.CategoryId == categoryId,
            "source-is" => Same(value.Source, args.GetValueOrDefault("source")),
            _ => false
        });
    }

    public async Task<Result<ConnectionActionResult>> Execute(ConnectionScope scope, ConnectionAction action, CancellationToken ct)
    {
        if (Catalog.FirstOrDefault(entry => entry.NodeKind == action.Target.Kind)?.ActionChoices.Any(choice => choice.Kind == action.Kind) != true)
            return Result<ConnectionActionResult>.Failure(Invalid("Choose a supported action for this node."));
        if (!await CanManage(scope, action.Target.Kind, ct)) return Result<ConnectionActionResult>.Failure(Forbidden);
        try
        {
            return Tool(action.Target.Kind) switch
            {
                "todo" => await ExecuteTodo(scope, action, ct),
                "bug-reports" => await ExecuteBug(scope, action, ct),
                { } tool => await ExecuteDocument(scope, tool, action, ct),
                _ => Result<ConnectionActionResult>.Failure(Missing)
            };
        }
        catch (ArgumentException error) { return Result<ConnectionActionResult>.Failure(Invalid(error.Message)); }
        catch (JsonException) { return Result<ConnectionActionResult>.Failure(Invalid("Enter valid values in the action fields.")); }
    }

    private async Task<Result<ConnectionActionResult>> ExecuteDocument(ConnectionScope scope, string tool, ConnectionAction action, CancellationToken ct)
    {
        var previous = await LoadDocument(scope, tool, ct);
        if (action.Target.Kind != tool && !ConnectionDocuments.Snapshots(tool, previous.Document ?? default).Any(node => node.Reference == action.Target))
            return Result<ConnectionActionResult>.Failure(Missing);
        if (tool == "sdlc" && OptionalGuid(action.Arguments, "ownerId") is { } owner &&
            (await organizations.GetByIdAsync(scope.OrganizationId, ct))?.HasMember(owner) != true)
            return Result<ConnectionActionResult>.Failure(Invalid("Choose a current organization member."));
        var document = previous.Document is { } saved ? JsonNode.Parse(saved.GetRawText())!.AsObject() : ConnectionDocumentActions.Empty(tool);
        var mutation = ConnectionDocumentActions.Apply(tool, document, action, scope.UserId, clock.GetUtcNow());
        if (!mutation.Changed) return Result<ConnectionActionResult>.Success(new("No change"));
        var result = await documents.Save(scope.OrganizationId, scope.ProjectId, scope.UserId, tool,
            new(previous.Revision, JsonSerializer.SerializeToElement(document)), ct);
        return Outcome(result, mutation.Node);
    }

    private async Task<Result<ConnectionActionResult>> ExecuteTodo(ConnectionScope scope, ConnectionAction action, CancellationToken ct)
    {
        var (organizationId, projectId, userId) = scope;
        var node = action.Target;
        var args = action.Arguments;
        if (action.Kind == "create-board")
        {
            var result = await todos.Create(organizationId, projectId, userId,
                new(Required(args, "name"), args.GetValueOrDefault("description"), Date(args, "targetDate"), Date(args, "startDate")), ct);
            return Outcome(result, result.Value is { } value ? new("todo-board", value.Id.ToString()) : null);
        }
        var board = await FindBoard(scope, node, ct);
        if (board is null) return Result<ConnectionActionResult>.Failure(Missing);
        if (action.Kind == "create-task")
        {
            var result = await todos.AddTask(organizationId, projectId, board.Id, userId, new(Required(args, "title"),
                args.GetValueOrDefault("description") ?? "", Required(args, "status"), OptionalGuid(args, "userId"),
                TargetDate: Date(args, "targetDate"), StartDate: Date(args, "startDate")), ct);
            return Outcome(result, result.Value is { } value ? new("todo-task", value.Id.ToString(), board.Id.ToString()) : null);
        }
        if (node.Kind == "todo-board" && action.Kind == "set-dates")
            return Outcome(await todos.UpdateDates(organizationId, projectId, board.Id, userId,
                new(Date(args, "targetDate"), Date(args, "startDate"), board.BoardRevision), ct));
        if (action.Kind == "create-column")
        {
            var id = $"status-{Guid.NewGuid():N}"[..20];
            var columns = board.Columns.Append(new TodoColumn(id, Required(args, "name"))).ToArray();
            return Outcome(await todos.ConfigureColumns(organizationId, projectId, board.Id, userId, new(columns, board.BoardRevision), ct),
                new("todo-column", id, board.Id.ToString()));
        }
        if (node.Kind == "todo-column" && action.Kind is "rename" or "reorder-column" or "delete")
        {
            var columns = board.Columns.ToList();
            var index = columns.FindIndex(column => column.Id == node.Id);
            if (index < 0) return Result<ConnectionActionResult>.Failure(Missing);
            var column = columns[index];
            if (action.Kind == "rename") columns[index] = column with { Name = Required(args, "name") };
            else if (action.Kind == "delete") columns.RemoveAt(index);
            else
            {
                if (!int.TryParse(Required(args, "position"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var position) || position < 1 || position > columns.Count)
                    return Result<ConnectionActionResult>.Failure(Invalid("Choose a valid position on this board."));
                columns.RemoveAt(index);
                columns.Insert(position - 1, column);
            }
            return Outcome(await todos.ConfigureColumns(organizationId, projectId, board.Id, userId, new(columns, board.BoardRevision), ct));
        }

        var taskId = action.Kind == "move-here" ? Required(args, "taskId") : node.Id;
        var task = board.Tasks.FirstOrDefault(item => item.Id.ToString() == taskId);
        if (task is null) return Result<ConnectionActionResult>.Failure(Missing);
        if (action.Kind is "move-here" or "move-task")
        {
            var status = action.Kind == "move-here" ? node.Id! : Required(args, "status");
            return task.Status == status ? Result<ConnectionActionResult>.Success(new("No change")) :
                Outcome(await todos.MoveTask(organizationId, projectId, board.Id, task.Id, userId, new(status), ct));
        }
        if (action.Kind is "assign-task" or "unassign-task")
        {
            var person = OptionalGuid(args, "userId") ?? throw new ArgumentException("Choose a person.");
            return Outcome(await todos.ChangeAssignee(organizationId, projectId, board.Id, task.Id, userId, new(person, action.Kind == "unassign-task"), ct));
        }
        if (action.Kind == "delete") return Outcome(await todos.DeleteTask(organizationId, projectId, board.Id, task.Id, userId, new(board.BoardRevision), ct));
        var edit = action.Kind switch
        {
            "set-title" => new UpdateTodoTaskRequest(Required(args, "title"), task.Description, task.Status, task.AssigneeIds, board.BoardRevision, task.TargetDate, task.StartDate),
            "set-description" => new UpdateTodoTaskRequest(task.Title, args.GetValueOrDefault("description"), task.Status, task.AssigneeIds, board.BoardRevision, task.TargetDate, task.StartDate),
            "set-dates" => new UpdateTodoTaskRequest(task.Title, task.Description, task.Status, task.AssigneeIds, board.BoardRevision, Date(args, "targetDate"), Date(args, "startDate")),
            _ => throw new ArgumentException("Choose a supported TODO action.")
        };
        return Outcome(await todos.UpdateTask(organizationId, projectId, board.Id, task.Id, userId, edit, ct));
    }

    private async Task<Result<ConnectionActionResult>> ExecuteBug(ConnectionScope scope, ConnectionAction action, CancellationToken ct)
    {
        BugScope bugScope = new(scope.OrganizationId, scope.ProjectId, scope.UserId);
        var args = action.Arguments;
        if (action.Kind == "create-category")
        {
            var createdCategory = await bugs.SaveCategory(bugScope, null, Required(args, "name"), false, ct);
            return Outcome(createdCategory, createdCategory.Value is { } category ? new("bug-category", category.Id.ToString()) : null);
        }
        if (!Guid.TryParse(action.Target.Id, out var id)) return Result<ConnectionActionResult>.Failure(Missing);
        if (action.Target.Kind == "bug-category")
        {
            var category = await db.BugCategories.AsNoTracking().SingleOrDefaultAsync(item => item.ProjectId == scope.ProjectId && item.Id == id, ct);
            return category is null ? Result<ConnectionActionResult>.Failure(Missing) : Outcome(await bugs.SaveCategory(bugScope, id,
                action.Kind == "rename" ? Required(args, "name") : category.Name, action.Kind == "archive" || action.Kind != "restore" && category.Archived, ct));
        }
        var fetched = await bugs.Get(bugScope, id, ct);
        if (!fetched.IsSuccess) return Result<ConnectionActionResult>.Failure(fetched.Error!);
        var report = fetched.Value!;
        var request = new TriageBugRequest(report.Revision, action.Kind == "set-status" ? Required(args, "status") : report.Status,
            action.Kind == "set-priority" ? Required(args, "priority") : report.Priority, action.Kind == "set-category" ? OptionalGuid(args, "categoryId") : report.CategoryId,
            action.Kind switch { "set-triage-note" => args.GetValueOrDefault("note", ""),
                "append-triage-note" => string.Join("\n\n", new[] { report.InternalNotes, Required(args, "note") }.Where(text => text.Length > 0)), _ => report.InternalNotes });
        return Outcome(await bugs.Triage(bugScope, id, request, ct));
    }

    private static Result<ConnectionActionResult> Outcome<T>(Result<T> result, ConnectionNodeRef? created = null) => result.IsSuccess
        ? Result<ConnectionActionResult>.Success(new(CreatedNode: created)) : Result<ConnectionActionResult>.Failure(result.Error!);

    internal static string Required(IReadOnlyDictionary<string, string> args, string key) =>
        args.GetValueOrDefault(key)?.Trim() is { Length: > 0 } value ? value : throw new ArgumentException($"Set the {key} field.");

    private static Guid? OptionalGuid(IReadOnlyDictionary<string, string> args, string key) =>
        string.IsNullOrWhiteSpace(args.GetValueOrDefault(key)) ? null : Guid.TryParse(args.GetValueOrDefault(key), out var value)
            ? value : throw new ArgumentException($"Choose a valid {key}.");

    private static DateOnly? Date(IReadOnlyDictionary<string, string> args, string key) =>
        string.IsNullOrWhiteSpace(args.GetValueOrDefault(key)) ? null : DateOnly.TryParseExact(args.GetValueOrDefault(key), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
            ? value : throw new ArgumentException($"Enter a valid {key}.");
}
