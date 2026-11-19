using System.Globalization;
using System.Text.Json;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Connections;
using DevHub.Application.Ideas;
using DevHub.Domain.Organizations;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Connections;

internal sealed class ConnectionAuthorization(IIdeasService ideas, IOrganizationRepository organizations)
{
    public Task<bool> CanView(ConnectionScope scope, CancellationToken ct) =>
        ideas.CanView(scope.OrganizationId, scope.ProjectId, scope.UserId, ct);

    public async Task<bool> HasManagePermission(ConnectionScope scope, CancellationToken ct) =>
        (await organizations.GetByIdAsync(scope.OrganizationId, ct))?.HasPermission(scope.UserId, OrganizationPermissions.ManageConnections) == true;
}

internal sealed class ProjectConnections(ApplicationDbContext db, ConnectionAuthorization access, IConnectionTools tools, IConnectionCalendarSchedule schedules, TimeProvider clock) : IProjectConnections
{
    private const int MaxConnections = 100;
    private const int MaxNodes = 24;
    private const int MaxRules = 16;
    private const int MaxSteps = 12;
    private const int MaxPage = 1000;
    private const int PageSize = 50;
    internal static readonly Error Missing = new("Connections.NotFound", "The project or connection was not found.", ErrorType.NotFound);
    private static readonly Error Forbidden = new("Connections.Forbidden", "You need permission to manage connections and apply the configured actions.", ErrorType.Forbidden);
    private static readonly Error Conflict = new("Connections.Conflict", "This connection changed. Reload before saving it again.", ErrorType.Conflict);
    internal static Error Invalid(string detail) => new("Connections.Invalid", detail, ErrorType.Validation);
    private DbSet<StoredConnection> Definitions => db.Set<StoredConnection>();

    public async Task<Result<ConnectionAccessResponse>> Access(ConnectionScope scope, CancellationToken ct)
    {
        if (!await access.CanView(scope, ct)) return Result<ConnectionAccessResponse>.Failure(Missing);
        return Result<ConnectionAccessResponse>.Success(new(await access.HasManagePermission(scope, ct)));
    }

    public async Task<Result<IReadOnlyList<ConnectionCapability>>> Catalog(ConnectionScope scope, CancellationToken ct) =>
        await access.CanView(scope, ct)
            ? Result<IReadOnlyList<ConnectionCapability>>.Success(tools.Catalog.Select(capability => capability with
            {
                ActionChoices = capability.ActionChoices.Select(choice => choice with { CreatedNodeKind = ConnectionBindings.CreatedKind(choice) }).ToArray()
            }).ToArray())
            : Result<IReadOnlyList<ConnectionCapability>>.Failure(Missing);

    public async Task<Result<IReadOnlyList<ConnectionNode>>> FindNodes(ConnectionScope scope, string? query, string? kind, string? id, string? parentId, int page, CancellationToken ct)
    {
        if (!await access.CanView(scope, ct)) return Result<IReadOnlyList<ConnectionNode>>.Failure(Missing);
        if (page is < 1 or > MaxPage || query?.Length > 150 || kind?.Length > 40)
            return Result<IReadOnlyList<ConnectionNode>>.Failure(Invalid("Use a valid page and search phrase."));
        var rootLookup = kind is not null && string.IsNullOrEmpty(query) && id is null && parentId is null
            && tools.Catalog.Any(entry => entry.NodeKind == kind && entry.Tool == kind);
        if (id is not null || rootLookup)
        {
            if (kind is null || !ValidReference(new(kind, id, parentId)))
                return Result<IReadOnlyList<ConnectionNode>>.Failure(Invalid("An exact lookup needs a node kind and ID."));
            var node = await tools.ResolveNode(scope, new(kind, id, parentId), ct);
            return Result<IReadOnlyList<ConnectionNode>>.Success(node is null ? [] : [node]);
        }
        var nodes = await tools.FindNodes(scope, query?.Trim(), ct);
        return Result<IReadOnlyList<ConnectionNode>>.Success(nodes.Where(node => string.IsNullOrEmpty(kind) || node.Reference.Kind == kind)
            .OrderBy(node => node.Reference.Id is not null).ThenBy(node => node.Path, StringComparer.OrdinalIgnoreCase).ThenBy(node => node.Reference.Id, StringComparer.Ordinal)
            .Skip((page - 1) * PageSize).Take(PageSize).ToArray());
    }

    public async Task<Result<IReadOnlyList<ConnectionResponse>>> List(ConnectionScope scope, CancellationToken ct)
    {
        if (!await access.CanView(scope, ct)) return Result<IReadOnlyList<ConnectionResponse>>.Failure(Missing);
        var definitions = await Active(scope).AsNoTracking().OrderByDescending(connection => connection.UpdatedAtUtc)
            .Take(MaxConnections).ToArrayAsync(ct);
        if (definitions.Length == 0) return Result<IReadOnlyList<ConnectionResponse>>.Success([]);
        var nodes = await tools.FindNodes(scope, null, ct);
        var byReference = nodes.ToDictionary(node => node.Reference);
        return Result<IReadOnlyList<ConnectionResponse>>.Success(definitions.Select(connection => ToResponse(connection, byReference)).ToArray());
    }

    public async Task<Result<ConnectionResponse>> Get(ConnectionScope scope, Guid id, CancellationToken ct)
    {
        if (!await access.CanView(scope, ct)) return Result<ConnectionResponse>.Failure(Missing);
        var connection = await Active(scope).AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct);
        if (connection is null) return Result<ConnectionResponse>.Failure(Missing);
        var nodes = await tools.FindNodes(scope, null, ct);
        return Result<ConnectionResponse>.Success(ToResponse(connection, nodes.ToDictionary(node => node.Reference)));
    }

    public async Task<Result<ConnectionDraftCheck>> Validate(ConnectionScope scope, Guid? id, SaveConnectionRequest request, CancellationToken ct)
    {
        var draft = await Prepare(scope, id, request, ct);
        if (!draft.IsSuccess) return Result<ConnectionDraftCheck>.Failure(draft.Error!);
        var checks = new List<ConnectionRuleCheck>();
        foreach (var rule in request.Rules) checks.Add(await ConnectionConditionEvaluation.Check(tools, scope, rule, ct));
        return Result<ConnectionDraftCheck>.Success(new(checks));
    }

    private async Task<Result<PreparedConnection>> Prepare(ConnectionScope scope, Guid? id, SaveConnectionRequest request, CancellationToken ct)
    {
        var permission = await RequireManage(scope, ct);
        if (permission is not null) return Result<PreparedConnection>.Failure(permission);

        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 150 || request.Revision < 0
            || request.Nodes is null || request.Nodes.Count is < 2 or > MaxNodes || request.Nodes.Any(node => !ValidReference(node))
            || request.Nodes.Distinct().Count() != request.Nodes.Count || request.Rules is null || request.Rules.Count > MaxRules
            || request.Rules.Any(rule => rule is null || rule.Id == Guid.Empty) || request.Rules.Select(rule => rule.Id).Distinct().Count() != request.Rules.Count)
            return Result<PreparedConnection>.Failure(Invalid($"Enter a title and select 2–{MaxNodes} distinct nodes. Each rule needs a unique ID."));

        var connection = id is { } existingId ? await Active(scope).SingleOrDefaultAsync(row => row.Id == existingId, ct) : null;
        if (id is not null && connection is null) return Result<PreparedConnection>.Failure(Missing);
        if (request.Revision != (connection?.Revision ?? 0) || request.Revision == int.MaxValue)
            return Result<PreparedConnection>.Failure(Conflict);
        if (connection is null && await Active(scope).CountAsync(ct) >= MaxConnections)
            return Result<PreparedConnection>.Failure(Invalid($"This project already has {MaxConnections} connections."));

        var available = (await tools.FindNodes(scope, null, ct)).ToDictionary(node => node.Reference);
        var saved = connection?.Nodes.ToDictionary(node => node.Reference) ?? [];
        var nodes = new Dictionary<ConnectionNodeRef, ConnectionNode>();
        foreach (var reference in request.Nodes)
        {
            if (available.TryGetValue(reference, out var node)) nodes.Add(reference, node);
            else if (saved.TryGetValue(reference, out var previous)) nodes.Add(reference, previous with { Unavailable = true, Options = null });
            else return Result<PreparedConnection>.Failure(Invalid("A selected node is unavailable in this project."));
        }

        var validation = await ValidateRules(scope, request.Rules, nodes, ct);
        return validation is null ? Result<PreparedConnection>.Success(new(connection, nodes)) : Result<PreparedConnection>.Failure(validation);
    }

    public async Task<Result<ConnectionResponse>> Save(ConnectionScope scope, Guid? id, SaveConnectionRequest request, CancellationToken ct)
    {
        var prepared = await Prepare(scope, id, request, ct);
        if (!prepared.IsSuccess) return Result<ConnectionResponse>.Failure(prepared.Error!);
        var (connection, nodes) = prepared.Value!;

        var events = db.Set<QueuedConnectionEvent>().Where(evt => evt.ProjectId == scope.ProjectId);
        var position = await events.MaxAsync(evt => (long?)evt.Position, ct) ?? 0;
        var pendingFrom = await events.Where(evt => evt.FinishedAtUtc == null).MinAsync(evt => (long?)evt.Position, ct);
        var now = clock.GetUtcNow();
        var previousVersions = connection?.Rules ?? [];
        var previousRules = previousVersions.Where(version => version.ActiveThrough is null).ToDictionary(version => version.Rule.Id);
        var nextRevision = request.Revision + 1;
        var rules = request.Rules.Select(rule =>
        {
            previousRules.TryGetValue(rule.Id, out var previous);
            if (previous is not null && previous.OwnerUserId == scope.UserId && SameRule(previous.Rule, rule)) return previous;
            var continuing = previous is not null && previous.OwnerUserId == scope.UserId && previous.Rule.Enabled && rule.Enabled
                && previous.Rule.RunOnce == rule.RunOnce;
            return new StoredRule(rule, position, null, continuing ? previous!.ActivationId : Guid.NewGuid(),
                nextRevision, scope.UserId, now, continuing ? previous!.TriggeredRunId : null);
        }).ToArray();
        var history = previousVersions.Where(version => version.Rule.Enabled && !rules.Contains(version))
            .Select(version => version.ActiveThrough is null ? version with { ActiveThrough = position } : version)
            .Where(version => version.ActiveThrough > version.ActiveAfter && version.ActiveThrough >= pendingFrom);

        if (connection is null)
        {
            connection = new() { Id = Guid.NewGuid(), ProjectId = scope.ProjectId };
            Definitions.Add(connection);
        }
        connection.OwnerUserId = scope.UserId;
        connection.Title = request.Title.Trim();
        connection.NodesJson = ConnectionStorage.Write(nodes.Values.Select(node => node with { Options = null }));
        connection.RulesJson = ConnectionStorage.Write(rules.Concat(history));
        connection.Revision = nextRevision;
        connection.UpdatedAtUtc = now;
        if (rules.Any(rule => rule.Rule.Enabled && ConnectionCalendarSchedule.IsScheduled(rule.Rule.Event)))
        {
            var json = await db.ProjectDocuments.AsNoTracking().Where(document => document.ProjectId == scope.ProjectId && document.ToolType == "calendar")
                .Select(document => document.ContentJson).SingleOrDefaultAsync(ct);
            if (json is not null)
            {
                using var calendar = JsonDocument.Parse(json);
                await schedules.Sync(scope.OrganizationId, scope.ProjectId, calendar.RootElement, ct);
            }
        }
        try { await db.SaveChangesAsync(ct); }
        catch (PersistenceConcurrencyException) { return Result<ConnectionResponse>.Failure(Conflict); }
        return Result<ConnectionResponse>.Success(ToResponse(connection, nodes));
    }

    public async Task<Result<bool>> Delete(ConnectionScope scope, Guid id, int revision, CancellationToken ct)
    {
        var permission = await RequireManage(scope, ct);
        if (permission is not null) return Result<bool>.Failure(permission);
        var connection = await Active(scope).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (connection is null) return Result<bool>.Failure(Missing);
        if (connection.Revision != revision || revision == int.MaxValue) return Result<bool>.Failure(Conflict);
        connection.Deleted = true;
        connection.Revision++;
        connection.UpdatedAtUtc = clock.GetUtcNow();
        try { await db.SaveChangesAsync(ct); }
        catch (PersistenceConcurrencyException) { return Result<bool>.Failure(Conflict); }
        return Result<bool>.Success(true);
    }

    public async Task<Result<IReadOnlyList<ConnectionRunResponse>>> Runs(ConnectionScope scope, Guid? connectionId, int page, CancellationToken ct)
    {
        if (!await access.CanView(scope, ct)) return Result<IReadOnlyList<ConnectionRunResponse>>.Failure(Missing);
        if (page is < 1 or > MaxPage) return Result<IReadOnlyList<ConnectionRunResponse>>.Failure(Invalid("Use a valid page number."));
        var runs = await db.Set<StoredConnectionRun>().AsNoTracking()
            .Where(run => run.ProjectId == scope.ProjectId && (connectionId == null || run.ConnectionId == connectionId))
            .OrderByDescending(run => run.OccurredAtUtc).ThenByDescending(run => run.Id)
            .Skip((page - 1) * PageSize).Take(PageSize).ToArrayAsync(ct);
        return Result<IReadOnlyList<ConnectionRunResponse>>.Success(runs.Select(run => run.Response()).ToArray());
    }

    private IQueryable<StoredConnection> Active(ConnectionScope scope) =>
        Definitions.Where(connection => connection.ProjectId == scope.ProjectId && !connection.Deleted);

    private async Task<Error?> RequireManage(ConnectionScope scope, CancellationToken ct)
    {
        if (!await access.CanView(scope, ct)) return Missing;
        return await access.HasManagePermission(scope, ct) ? null : Forbidden;
    }

    private async Task<Error?> ValidateRules(ConnectionScope scope, IReadOnlyList<ConnectionRule> rules,
        IReadOnlyDictionary<ConnectionNodeRef, ConnectionNode> nodes, CancellationToken ct)
    {
        var catalog = tools.Catalog.ToDictionary(entry => entry.NodeKind);
        var authorizedKinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.Title) || rule.Title.Trim().Length > 150 || !ValidReference(rule.Source)
                || !nodes.TryGetValue(rule.Source, out var source) || !catalog.TryGetValue(rule.Source.Kind, out var trigger)
                || !ValidateChoice(rule.Event, rule.EventArguments, trigger.EventChoices, source)
                || rule.Conditions is null || rule.Conditions.Count > MaxSteps || rule.Actions is null || rule.Actions.Count is < 1 or > MaxSteps
                || rule.Actions.Any(action => action is null || action.Id == Guid.Empty)
                || rule.Actions.Select(action => action.Id).Distinct().Count() != rule.Actions.Count)
                return Invalid("Give each rule a title, an available event, and at least one action. Check its event fields.");

            if (source.Unavailable && rule.Enabled)
                return Invalid("Pause rules that reference missing nodes, or replace the missing nodes before enabling them.");
            var eventFields = trigger.EventChoices.Single(choice => choice.Kind == rule.Event).Fields;
            foreach (var condition in rule.Conditions)
            {
                if (condition is null || !ValidReference(condition.Node) || !nodes.TryGetValue(condition.Node, out var node)
                    || !catalog.TryGetValue(condition.Node.Kind, out var capability) || (rule.Enabled && node.Unavailable)
                    || !ValidateChoice(condition.Kind, condition.Arguments, capability.ConditionChoices, node))
                    return Invalid("Check the node, fields, and values of each condition.");
            }
            var earlierOutputs = new Dictionary<Guid, string?>();
            foreach (var action in rule.Actions)
            {
                if (!ValidReference(action.Target) || !catalog.TryGetValue(action.Target.Kind, out var capability))
                    return Invalid("Select a valid action target.");
                ConnectionNode node;
                if (action.TargetFromStepId is { } previousId)
                {
                    if (action.Target.Id is not null || action.Target.ParentId is not null ||
                        !earlierOutputs.TryGetValue(previousId, out var createdKind) || createdKind != action.Target.Kind)
                        return Invalid("Select an earlier action that creates an item of the expected type.");
                    node = new(action.Target, capability.Label, capability.Label);
                }
                else if (!nodes.TryGetValue(action.Target, out node!) || (rule.Enabled && node.Unavailable))
                    return Invalid("Select an available node as the action target.");

                var actionChoice = capability.ActionChoices.FirstOrDefault(choice => choice.Kind == action.Kind);
                var bindings = action.ArgumentBindings;
                if (actionChoice is null || !ValidateChoice(action.Kind, action.Arguments, capability.ActionChoices, node, bindings)
                    || (bindings is not null && bindings.Any(binding =>
                        actionChoice.Fields.FirstOrDefault(field => field.Key == binding.Key) is not { } targetField ||
                        eventFields.FirstOrDefault(field => field.Key == binding.Value?.EventField) is not { } eventField ||
                        !ConnectionBindings.Compatible(targetField, eventField))))
                    return Invalid("Check the node, fields, and values of each action.");
                if (authorizedKinds.Add(action.Target.Kind) && !await tools.CanManage(scope, action.Target.Kind, ct)) return Forbidden;
                earlierOutputs.Add(action.Id, ConnectionBindings.CreatedKind(actionChoice));
            }
        }
        return null;
    }

    private static bool ValidReference(ConnectionNodeRef? reference) => reference is not null
        && !string.IsNullOrEmpty(reference.Kind) && reference.Kind.Length <= 40
        && reference.Id is not ("" or { Length: > 100 }) && reference.ParentId is not ("" or { Length: > 100 });

    internal static bool ValidateChoice(string kind, IReadOnlyDictionary<string, string>? arguments, IReadOnlyList<ConnectionChoice> choices, ConnectionNode node,
        IReadOnlyDictionary<string, ConnectionArgumentBinding>? bindings = null)
    {
        var choice = choices.FirstOrDefault(item => item.Kind == kind);
        if (choice is null || arguments is null || arguments.Keys.Any(key => choice.Fields.All(field => field.Key != key))) return false;
        foreach (var field in choice.Fields)
        {
            if (bindings?.ContainsKey(field.Key) == true)
            {
                if (arguments.ContainsKey(field.Key)) return false;
                continue;
            }
            if (!arguments.TryGetValue(field.Key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                if (field.Required) return false;
                continue;
            }
            if (value.Length > 10000) return false;
            var options = field.Options ?? node.Options?.GetValueOrDefault(field.Key) ?? node.Options?.GetValueOrDefault(field.Type);
            if (options is { Count: > 0 } && options.All(option => option.Value != value)) return false;
            if (field.Type == "date" && !DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return false;
            if (field.Type == "number" && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) return false;
            if (field.Type == "boolean" && value is not ("true" or "false")) return false;
            if (field.Key == "minutesBefore" && !ConnectionCalendarSchedule.TryOffset(value, out _)) return false;
        }
        return true;
    }

    private static bool SameRule(ConnectionRule previous, ConnectionRule next) =>
        ConnectionStorage.Write(previous) == ConnectionStorage.Write(next);

    private static ConnectionResponse ToResponse(StoredConnection connection, IReadOnlyDictionary<ConnectionNodeRef, ConnectionNode> nodes)
    {
        var rules = connection.Rules.Where(version => version.ActiveThrough is null).ToArray();
        return new(connection.Id, connection.Title, connection.Revision, connection.OwnerUserId,
            connection.Nodes.Select(node => nodes.GetValueOrDefault(node.Reference) ?? node with { Unavailable = true }).ToArray(),
            rules.Select(version => version.Rule).ToArray(), connection.UpdatedAtUtc,
            rules.Where(version => version.Rule.RunOnce && version.TriggeredRunId is not null).Select(version => version.Rule.Id).ToArray());
    }

    private sealed record PreparedConnection(StoredConnection? Connection, Dictionary<ConnectionNodeRef, ConnectionNode> Nodes);
}
