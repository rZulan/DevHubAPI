using DevHub.Application.Common;
using System.Text.Json;

namespace DevHub.Application.Connections;

public sealed record ConnectionScope(Guid OrganizationId, Guid ProjectId, Guid UserId);
public sealed record ConnectionNodeRef(string Kind, string? Id = null, string? ParentId = null);
public sealed record ConnectionOption(string Value, string Label);
public sealed record ConnectionNode(ConnectionNodeRef Reference, string Label, string Path,
    IReadOnlyDictionary<string, IReadOnlyList<ConnectionOption>>? Options = null, bool Unavailable = false);
public sealed record ConnectionField(string Key, string Label, string Type, bool Required = true, IReadOnlyList<ConnectionOption>? Options = null);
public sealed record ConnectionChoice(string Kind, string Label, IReadOnlyList<ConnectionField> Fields, string? CreatedNodeKind = null);
public sealed record ConnectionCapability(string NodeKind, string Tool, string Label, IReadOnlyList<ConnectionChoice> EventChoices,
    IReadOnlyList<ConnectionChoice> ConditionChoices, IReadOnlyList<ConnectionChoice> ActionChoices);
public sealed record ConnectionCondition(ConnectionNodeRef Node, string Kind, IReadOnlyDictionary<string, string> Arguments);
public sealed record ConnectionArgumentBinding(string EventField);
public sealed record ConnectionAction(Guid Id, ConnectionNodeRef Target, string Kind, IReadOnlyDictionary<string, string> Arguments,
    Guid? TargetFromStepId = null, IReadOnlyDictionary<string, ConnectionArgumentBinding>? ArgumentBindings = null);
public sealed record ConnectionRule(Guid Id, string Title, bool Enabled, bool IncludeAutomation, ConnectionNodeRef Source,
    string Event, IReadOnlyDictionary<string, string> EventArguments, bool MatchAny, IReadOnlyList<ConnectionCondition> Conditions,
    IReadOnlyList<ConnectionAction> Actions, bool RunOnce = false);
public sealed record SaveConnectionRequest(string Title, int Revision, IReadOnlyList<ConnectionNodeRef> Nodes, IReadOnlyList<ConnectionRule> Rules);
public sealed record ConnectionResponse(Guid Id, string Title, int Revision, Guid OwnerUserId, IReadOnlyList<ConnectionNode> Nodes,
    IReadOnlyList<ConnectionRule> Rules, DateTimeOffset UpdatedAtUtc, IReadOnlyList<Guid>? TriggeredRuleIds = null);
public sealed record ConnectionAccessResponse(bool CanManage);
public sealed record ConnectionConditionCheck(ConnectionNodeRef Node, string Kind, bool? Passed, string? Detail = null);
public sealed record ConnectionRuleCheck(Guid RuleId, string Status, IReadOnlyList<ConnectionConditionCheck> Conditions);
public sealed record ConnectionDraftCheck(IReadOnlyList<ConnectionRuleCheck> Rules);
public sealed record ConnectionStepResponse(Guid ActionId, string Label, string Status, string? Detail = null);
public sealed record ConnectionRunResponse(Guid Id, Guid ConnectionId, Guid RuleId, string RuleTitle, string Event,
    ConnectionNodeRef Source, DateTimeOffset OccurredAtUtc, string Status, string? Detail, IReadOnlyList<ConnectionStepResponse> Steps);
public sealed record ConnectionEventDraft(ConnectionNodeRef Node, string Kind, IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<ConnectionNodeRef> Ancestors, DateTimeOffset? OccurredAtUtc = null);
public sealed record ConnectionActionResult(string? Detail = null, ConnectionNodeRef? CreatedNode = null);

public interface IConnectionEvents
{
    Task Enqueue(Guid organizationId, Guid projectId, Guid actorUserId, ConnectionEventDraft draft, CancellationToken ct);
}

public interface IConnectionChangeNotifier
{
    Task Changed(ConnectionScope scope, ConnectionNodeRef target, CancellationToken ct);
}

public interface IConnectionCalendarSchedule
{
    Task Sync(Guid organizationId, Guid projectId, JsonElement document, CancellationToken ct);
}

public interface IConnectionTools
{
    IReadOnlyList<ConnectionCapability> Catalog { get; }
    Task<IReadOnlyList<ConnectionNode>> FindNodes(ConnectionScope scope, string? query, CancellationToken ct);
    Task<ConnectionNode?> ResolveNode(ConnectionScope scope, ConnectionNodeRef reference, CancellationToken ct);
    Task<bool> CanManage(ConnectionScope scope, string targetKind, CancellationToken ct);
    Task<Result<bool>> Check(ConnectionScope scope, ConnectionCondition condition, CancellationToken ct);
    Task<Result<ConnectionActionResult>> Execute(ConnectionScope scope, ConnectionAction action, CancellationToken ct);
}

public interface IProjectConnections
{
    Task<Result<ConnectionAccessResponse>> Access(ConnectionScope scope, CancellationToken ct);
    Task<Result<bool>> SetTodoCalendarEnabled(ConnectionScope scope, bool enabled, CancellationToken ct);
    Task<Result<IReadOnlyList<ConnectionCapability>>> Catalog(ConnectionScope scope, CancellationToken ct);
    Task<Result<IReadOnlyList<ConnectionNode>>> FindNodes(ConnectionScope scope, string? query, string? kind, string? id, string? parentId, int page, CancellationToken ct);
    Task<Result<IReadOnlyList<ConnectionResponse>>> List(ConnectionScope scope, CancellationToken ct);
    Task<Result<ConnectionResponse>> Get(ConnectionScope scope, Guid id, CancellationToken ct);
    Task<Result<ConnectionDraftCheck>> Validate(ConnectionScope scope, Guid? id, SaveConnectionRequest request, CancellationToken ct);
    Task<Result<ConnectionResponse>> Save(ConnectionScope scope, Guid? id, SaveConnectionRequest request, CancellationToken ct);
    Task<Result<bool>> Delete(ConnectionScope scope, Guid id, int revision, CancellationToken ct);
    Task<Result<IReadOnlyList<ConnectionRunResponse>>> Runs(ConnectionScope scope, Guid? connectionId, int page, CancellationToken ct);
}
