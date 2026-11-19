using DevHub.Application.Common;
using DevHub.Application.Connections;

namespace DevHub.Infrastructure.Connections;

internal static class ConnectionBindings
{
    private static readonly Dictionary<string, string> CreatedKinds = new(StringComparer.Ordinal)
    {
        ["create-board"] = "todo-board", ["create-task"] = "todo-task", ["create-column"] = "todo-column",
        ["create-table"] = "erd-table", ["add-column"] = "erd-column",
        ["add-relationship"] = "erd-relationship", ["create-entry"] = "calendar-entry", ["create-category"] = "bug-category",
        ["create-note"] = "sticky-note", ["create-page"] = "wiki-page", ["create-subpage"] = "wiki-page", ["create-screen"] = "ui-screen",
        ["create-layer"] = "ui-layer", ["duplicate-screen"] = "ui-screen", ["duplicate-layer"] = "ui-layer",
        ["create-diagram"] = "uml-diagram", ["duplicate-diagram"] = "uml-diagram", ["add-element"] = "uml-element",
        ["add-uml-relationship"] = "uml-relationship"
    };

    public static string? CreatedKind(ConnectionChoice choice) => choice.CreatedNodeKind ?? CreatedKinds.GetValueOrDefault(choice.Kind);

    public static bool Compatible(ConnectionField target, ConnectionField source)
    {
        if (target.Type != source.Type && (target.Type is not ("text" or "multiline") || source.Type is not ("text" or "multiline"))) return false;
        if (target.Options is not { Count: > 0 }) return true;
        return source.Options is { Count: > 0 } && source.Options.All(value => target.Options.Any(option => option.Value == value.Value));
    }

    public static Result<ConnectionAction> Resolve(ConnectionAction next, IReadOnlyList<StoredStep> receipts, ConnectionEventDraft evt)
    {
        var target = next.Target;
        if (next.TargetFromStepId is { } previousStep)
        {
            var created = receipts.FirstOrDefault(step => step.Response.ActionId == previousStep)?.CreatedNode;
            if (created is null || created.Kind != target.Kind)
                return Result<ConnectionAction>.Failure(ProjectConnections.Invalid("An earlier action did not return the item needed for this action."));
            target = created;
        }

        if (next.ArgumentBindings is not { Count: > 0 } bindings)
            return Result<ConnectionAction>.Success(next with { Target = target });

        var arguments = new Dictionary<string, string>(next.Arguments, StringComparer.Ordinal);
        foreach (var (key, binding) in bindings)
        {
            if (!evt.Fields.TryGetValue(binding.EventField, out var value) || string.IsNullOrWhiteSpace(value))
                return Result<ConnectionAction>.Failure(ProjectConnections.Invalid("The triggering event does not contain a field required by this action."));
            arguments[key] = value;
        }
        return Result<ConnectionAction>.Success(next with { Target = target, Arguments = arguments });
    }
}
