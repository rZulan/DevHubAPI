using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DevHub.Application.Connections;

namespace DevHub.Infrastructure.Connections;

internal static partial class ConnectionDocumentActions
{
    internal readonly record struct Mutation(bool Changed, ConnectionNodeRef? Node = null);

    internal static JsonObject Empty(string tool) => tool switch
    {
        "erd" => new() { ["tables"] = new JsonArray(), ["relationships"] = new JsonArray() },
        "calendar" => new() { ["timeZone"] = "UTC", ["events"] = new JsonArray() },
        "notes" => new() { ["notes"] = new JsonArray() },
        "wiki" => new() { ["pages"] = new JsonArray(), ["homePageId"] = null },
        "ui-editor" => new() { ["layers"] = new JsonArray() },
        "uml" => new() { ["version"] = 1, ["diagrams"] = new JsonArray() },
        "sdlc" => new() { ["model"] = "waterfall", ["currentPhase"] = 0, ["completed"] = false, ["notes"] = new JsonObject() },
        _ => throw new ArgumentException("Choose a supported project tool.")
    };

    internal static Mutation Apply(string tool, JsonObject document, ConnectionAction action, Guid actorId, DateTimeOffset now) => tool switch
    {
        "erd" => Erd(document, action),
        "calendar" => Calendar(document, action),
        "notes" => Notes(document, action, now),
        "wiki" => Wiki(document, action, actorId, now),
        "ui-editor" => Design(document, action),
        "uml" => Uml(document, action, now),
        "sdlc" => new(SdlcConnections.Apply(document, action)),
        _ => throw new ArgumentException("Choose a supported project tool.")
    };

    private static string NewId() => Guid.NewGuid().ToString();
    private static string? Text(JsonNode? item, string key) => item?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    private static string Arg(ConnectionAction action, string key) => ConnectionTools.Required(action.Arguments, key);
    private static string? Optional(ConnectionAction action, string key) => action.Arguments.GetValueOrDefault(key);
    private static JsonArray List(JsonNode document, string key) => document[key]?.AsArray() ?? throw new ArgumentException("The saved tool document is unavailable.");
    private static JsonObject Find(JsonArray items, string? id) => items.OfType<JsonObject>().FirstOrDefault(item => Text(item, "id") == id) ??
        throw new ArgumentException("The selected item is unavailable.");
    private static bool Flag(JsonNode item, string key) => item[key] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;
    private static bool Set(JsonObject item, string key, JsonNode? value)
    {
        if (item.ContainsKey(key) && JsonNode.DeepEquals(item[key], value)) return false;
        item[key] = value;
        return true;
    }

    private static Mutation Erd(JsonObject document, ConnectionAction action)
    {
        var tables = List(document, "tables");
        var relations = List(document, "relationships");
        if (action.Kind == "set-dialect") return new(Set(document, "dialect", Arg(action, "dialect")));
        if (action.Kind == "remove-table") return new(DeleteTable(tables, relations, Arg(action, "tableId")));
        if (action.Kind == "remove-relationship") return new(relations.Remove(Find(relations, Arg(action, "relationshipId"))));
        if (action.Kind == "create-table")
        {
            var name = Arg(action, "name");
            if (HasName(tables, name)) return new(false);
            var id = NewId();
            var fields = new JsonArray();
            if (Optional(action, "columns") is { Length: > 0 } raw)
            {
                if (JsonNode.Parse(raw) is not JsonArray supplied || supplied.Count > 200 || supplied.Any(field => field is not JsonObject))
                    throw new ArgumentException("Enter a JSON array of up to 200 column objects.");
                foreach (var entry in supplied.OfType<JsonObject>())
                {
                    var fieldName = Text(entry, "name")?.Trim() ?? throw new ArgumentException("Each column needs a name.");
                    if (HasName(fields, fieldName)) throw new ArgumentException("Use unique column names.");
                    fields.Add(NewColumn(fieldName, Text(entry, "type") ?? "UUID", Flag(entry, "primary"), !entry.ContainsKey("nullable") || Flag(entry, "nullable"),
                        Flag(entry, "unique"), Text(entry, "length") ?? "", Text(entry, "defaultValue") ?? ""));
                }
            }
            else fields.Add(NewColumn("id", "UUID", primary: true));
            tables.Add(new JsonObject { ["id"] = id, ["name"] = name, ["x"] = 48 + (tables.Count % 5) * 340,
                ["y"] = 48 + (tables.Count / 5) * 240, ["fields"] = fields });
            return new(true, new("erd-table", id));
        }
        if (action.Kind == "add-relationship")
        {
            var id = NewId();
            var relation = new JsonObject { ["id"] = id, ["cardinality"] = Arg(action, "cardinality"),
                ["onDelete"] = BlankToNull(Optional(action, "onDelete")) ?? "RESTRICT" };
            SetErdEndpoints(tables, relation, action);
            if (relations.OfType<JsonObject>().Any(item => new[] { "fromTable", "fromField", "toTable", "toField", "cardinality", "onDelete" }
                    .All(key => Text(item, key) == Text(relation, key)))) return new(false);
            relations.Add(relation);
            return new(true, new("erd-relationship", id));
        }
        if (action.Target.Kind == "erd-relationship")
        {
            var relation = Find(relations, action.Target.Id);
            return action.Kind switch
            {
                "set-delete-behavior" => new(Set(relation, "onDelete", Arg(action, "onDelete"))),
                "set-endpoints" => new(SetErdEndpoints(tables, relation, action)),
                "set-cardinality" => new(Set(relation, "cardinality", Arg(action, "cardinality")) | relation.Remove("leftEnd") | relation.Remove("rightEnd")),
                "delete" => new(relations.Remove(relation)),
                _ => throw new ArgumentException("Choose an ERD relationship action.")
            };
        }
        var table = Find(tables, action.Target.ParentId ?? action.Target.Id);
        var fieldsOnTable = List(table, "fields");
        if (action.Target.Kind == "erd-table")
        {
            if (action.Kind == "add-column")
            {
                var name = Arg(action, "name");
                if (HasName(fieldsOnTable, name)) return new(false);
                var column = NewColumn(name, Arg(action, "type"));
                fieldsOnTable.Add(column);
                return new(true, new("erd-column", Text(column, "id"), action.Target.Id));
            }
            if (action.Kind == "delete")
                return new(DeleteTable(tables, relations, action.Target.Id));
            return action.Kind switch
            {
                "rename" => new(RenameErd(tables, table, Arg(action, "name"))),
                "set-index" => new(SetErdIndex(table, action)),
                "set-check" => new(SetErdCheck(table, action)),
                "remove-index" => new(RemoveErdConstraint(table, "indexes", Arg(action, "constraintId"))),
                "remove-check" => new(RemoveErdConstraint(table, "checks", Arg(action, "checkId"))),
                _ => throw new ArgumentException("Choose a table action.")
            };
        }
        var field = Find(fieldsOnTable, action.Target.Id);
        if (action.Kind == "delete")
        {
            var columnId = action.Target.Id;
            var tableId = action.Target.ParentId;
            RemoveWhere(relations, relation => ReferencesColumn(relation, "from", tableId, columnId) || ReferencesColumn(relation, "to", tableId, columnId));
            if (table["indexes"] is JsonArray indexes)
                RemoveWhere(indexes, index => ContainsValue(index["fields"], columnId));
            return new(fieldsOnTable.Remove(field));
        }
        return action.Kind switch
        {
            "rename" => new(RenameErd(fieldsOnTable, field, Arg(action, "name"))),
            "set-type" => new(SetErdType(field, Arg(action, "type"))),
            "set-column-details" => new(SetErdColumnDetails(field, action)),
            _ => throw new ArgumentException("Choose an ERD column action.")
        };
    }

    private static bool SetErdEndpoints(JsonArray tables, JsonObject relation, ConnectionAction action)
    {
        var from = Find(tables, action.Target.Kind == "erd-table" ? action.Target.Id : Arg(action, "fromTable"));
        var to = Find(tables, Arg(action, "toTable"));
        var fromField = FindErdColumn(from, Arg(action, "fromField"));
        var toField = FindErdColumn(to, Arg(action, "toField"));
        return Set(relation, "fromTable", Text(from, "id")) | Set(relation, "fromField", Text(fromField, "id")) |
            Set(relation, "toTable", Text(to, "id")) | Set(relation, "toField", Text(toField, "id")) |
            relation.Remove("fromFields") | relation.Remove("toFields") | relation.Remove("fromAnchor") | relation.Remove("toAnchor");
    }

    private static JsonObject FindErdColumn(JsonObject table, string token)
    {
        var columns = List(table, "fields").OfType<JsonObject>().ToArray();
        return columns.FirstOrDefault(column => Text(column, "id") == token) ??
            columns.FirstOrDefault(column => string.Equals(Text(column, "name"), token, StringComparison.OrdinalIgnoreCase)) ??
            throw new ArgumentException("Choose an existing column in the selected table.");
    }

    private static bool SetErdType(JsonObject column, string type)
    {
        var changed = Set(column, "type", type);
        if (type is not ("INT" or "BIGINT") && Flag(column, "autoIncrement")) changed |= Set(column, "autoIncrement", false);
        return changed;
    }

    private static bool SetErdColumnDetails(JsonObject column, ConnectionAction action)
    {
        var changed = false;
        foreach (var field in new[] { "length", "defaultValue" })
            if (Optional(action, field) is { } value) changed |= Set(column, field, value);
        foreach (var field in new[] { "primary", "nullable", "unique", "autoIncrement" })
        {
            var value = Optional(action, field);
            if (value is "true" or "false") changed |= Set(column, field, value == "true");
        }
        if (Flag(column, "primary") || Flag(column, "autoIncrement")) changed |= Set(column, "nullable", false);
        if (Flag(column, "autoIncrement"))
        {
            if (Text(column, "type") is not ("INT" or "BIGINT")) throw new ArgumentException("Auto-increment requires an integer column.");
            changed |= Set(column, "defaultValue", "");
        }
        return changed;
    }

    private static bool SetErdIndex(JsonObject table, ConnectionAction action)
    {
        var fields = Arg(action, "columns").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(token => Text(FindErdColumn(table, token), "id")!).Distinct(StringComparer.Ordinal).Select(value => (JsonNode?)JsonValue.Create(value)).ToArray();
        if (fields.Length == 0) throw new ArgumentException("Choose at least one indexed column.");
        var constraint = FindOrAddConstraint(table, "indexes", Arg(action, "name"));
        return Set(constraint, "fields", new JsonArray(fields)) | Set(constraint, "unique", Optional(action, "unique") == "true");
    }

    private static bool SetErdCheck(JsonObject table, ConnectionAction action) =>
        Set(FindOrAddConstraint(table, "checks", Arg(action, "name")), "expression", Arg(action, "expression"));

    private static JsonObject FindOrAddConstraint(JsonObject table, string collection, string name)
    {
        if (table[collection] is not JsonArray constraints) table[collection] = constraints = new JsonArray();
        var existing = constraints.OfType<JsonObject>().FirstOrDefault(item => string.Equals(Text(item, "name"), name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return existing;
        var created = new JsonObject { ["id"] = NewId(), ["name"] = name };
        constraints.Add(created);
        return created;
    }

    private static bool RemoveErdConstraint(JsonObject table, string collection, string id)
    {
        if (table[collection] is not JsonArray constraints) throw new ArgumentException("The selected constraint is no longer available.");
        return constraints.Remove(Find(constraints, id));
    }

    private static bool HasName(JsonArray items, string name, JsonObject? except = null) => items.OfType<JsonObject>().Any(item =>
        !ReferenceEquals(item, except) && string.Equals(Text(item, "name")?.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));

    private static bool DeleteTable(JsonArray tables, JsonArray relations, string? tableId)
    {
        var table = Find(tables, tableId);
        RemoveWhere(relations, relation => Text(relation, "fromTable") == tableId || Text(relation, "toTable") == tableId);
        return tables.Remove(table);
    }

    private static bool RenameErd(JsonArray items, JsonObject item, string name)
    {
        if (HasName(items, name, item)) throw new ArgumentException("An item with this name already exists.");
        return Set(item, "name", name);
    }

    private static JsonObject NewColumn(string name, string type, bool primary = false, bool nullable = true, bool unique = false,
        string length = "", string defaultValue = "") => new()
    {
        ["id"] = NewId(), ["name"] = name, ["type"] = type, ["length"] = length, ["defaultValue"] = defaultValue,
        ["primary"] = primary, ["nullable"] = !primary && nullable, ["unique"] = unique
    };

    private static bool ReferencesColumn(JsonObject relation, string side, string? tableId, string? columnId) =>
        Text(relation, side + "Table") == tableId && (Text(relation, side + "Field") == columnId || ContainsValue(relation[side + "Fields"], columnId));

    private static bool ContainsValue(JsonNode? list, string? value) => list is JsonArray items &&
        items.Any(item => item is JsonValue field && field.TryGetValue<string>(out var id) && id == value);

    private static void RemoveWhere(JsonArray items, Func<JsonObject, bool> shouldRemove)
    {
        for (var index = items.Count - 1; index >= 0; index--)
            if (items[index] is JsonObject item && shouldRemove(item)) items.RemoveAt(index);
    }

    private static Mutation Calendar(JsonObject document, ConnectionAction action)
    {
        var entries = List(document, "events");
        if (action.Kind == "create-entry")
        {
            var id = NewId();
            var entry = new JsonObject { ["id"] = id, ["title"] = Arg(action, "title"), ["kind"] = "milestone", ["description"] = "" };
            SetCalendarFields(entry, action);
            entries.Add(entry);
            return new(true, new("calendar-entry", id));
        }
        var existing = Find(entries, action.Target.Id);
        return action.Kind switch
        {
            "set-title" => new(Set(existing, "title", Arg(action, "title"))),
            "reschedule" => new(SetCalendarFields(existing, action)),
            "delete" => new(entries.Remove(existing)),
            _ => throw new ArgumentException("Choose a calendar action.")
        };
    }

    private static bool SetCalendarFields(JsonObject item, ConnectionAction action)
    {
        var date = Arg(action, "date");
        var changed = Set(item, "startDate", date) | Set(item, "endDate", Optional(action, "endDate")?.Trim() is { Length: > 0 } endDate ? endDate : date);
        if (Optional(action, "kind") is { Length: > 0 } kind) changed |= Set(item, "kind", kind);
        if (Optional(action, "description") is { } description) changed |= Set(item, "description", description);
        var allDayChoice = Optional(action, "allDay");
        var from = Optional(action, "startTime");
        var to = Optional(action, "endTime");
        var timed = allDayChoice == "timed" || allDayChoice != "all-day" && (!string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to) || item.ContainsKey("allDay") && !Flag(item, "allDay"));
        var start = timed ? from ?? Text(item, "startTime") ?? "" : "";
        var end = timed ? to ?? Text(item, "endTime") ?? "" : "";
        return changed | Set(item, "allDay", !timed) | Set(item, "startTime", start) | Set(item, "endTime", end);
    }

    private static Mutation Notes(JsonObject document, ConnectionAction action, DateTimeOffset now)
    {
        var notes = List(document, "notes");
        if (action.Kind == "create-note")
        {
            var id = NewId();
            notes.Add(new JsonObject { ["id"] = id, ["title"] = Arg(action, "title"), ["body"] = Optional(action, "body") ?? "",
                ["color"] = Optional(action, "color") is { Length: > 0 } color ? color : null,
                ["emoji"] = Optional(action, "emoji") is { Length: > 0 } emoji ? emoji : null, ["updatedAt"] = now.ToString("O") });
            return new(true, new("sticky-note", id));
        }
        var note = Find(notes, action.Target.Id);
        if (action.Kind == "delete") return new(notes.Remove(note));
        var changed = action.Kind switch
        {
            "set-title" => Set(note, "title", Arg(action, "title")),
            "replace-body" => Set(note, "body", Optional(action, "body") ?? ""),
            "append-body" => Set(note, "body", Append(Text(note, "body"), Arg(action, "body"))),
            "set-color" => Set(note, "color", Arg(action, "color")),
            "set-emoji" => Set(note, "emoji", Optional(action, "emoji") is { Length: > 0 } emoji ? emoji : null),
            _ => throw new ArgumentException("Choose a note action.")
        };
        if (changed) note["updatedAt"] = now.ToString("O");
        return new(changed);
    }

    private static string Append(string? previous, string text) => string.IsNullOrEmpty(previous) ? text : $"{previous}\n\n{text}";

    private static Mutation Wiki(JsonObject document, ConnectionAction action, Guid actor, DateTimeOffset now)
    {
        var pages = List(document, "pages");
        if (action.Kind is "create-page" or "create-subpage")
        {
            var id = NewId();
            var title = Arg(action, "title");
            var basis = WikiSlug().Replace(title.Normalize(System.Text.NormalizationForm.FormKD).ToLowerInvariant(), "-").Trim('-');
            if (basis.Length > 90) basis = basis[..90].TrimEnd('-');
            if (basis.Length == 0) basis = "page";
            var slug = basis;
            for (var suffix = 2; pages.OfType<JsonObject>().Any(page => Text(page, "slug") == slug); suffix++) slug = $"{basis}-{suffix}";
            var parentId = action.Kind == "create-subpage" ? action.Target.Id : Optional(action, "parentId");
            pages.Add(new JsonObject { ["id"] = id, ["title"] = title, ["slug"] = slug, ["body"] = Optional(action, "body") ?? "",
                ["message"] = "Created by automation", ["authorId"] = actor.ToString(), ["updatedAt"] = now.ToString("O"),
                ["parentId"] = string.IsNullOrEmpty(parentId) ? null : parentId, ["history"] = new JsonArray() });
            if (document["homePageId"] is null) document["homePageId"] = id;
            return new(true, new("wiki-page", id));
        }
        var existing = Find(pages, action.Target.Id);
        if (action.Kind == "delete")
        {
            pages.Remove(existing);
            foreach (var child in pages.OfType<JsonObject>().Where(page => Text(page, "parentId") == action.Target.Id))
                child["parentId"] = Text(existing, "parentId");
            if (Text(document, "homePageId") == action.Target.Id) document["homePageId"] = Text(pages.FirstOrDefault(), "id");
            return new(true);
        }
        if (action.Kind == "set-home") return new(Set(document, "homePageId", action.Target.Id));
        var previous = new JsonObject { ["id"] = NewId() };
        foreach (var key in new[] { "title", "body", "message", "authorId", "updatedAt" }) previous[key] = existing[key]?.DeepClone();
        var changed = action.Kind switch
        {
            "set-title" => Set(existing, "title", Arg(action, "title")),
            "replace-body" => Set(existing, "body", Optional(action, "body") ?? ""),
            "append-body" => Set(existing, "body", Append(Text(existing, "body"), Arg(action, "body"))),
            "move-page" => Set(existing, "parentId", Optional(action, "parentId") is { Length: > 0 } parent ? parent : null),
            _ => throw new ArgumentException("Choose a wiki action.")
        };
        if (!changed) return new(false);
        existing["updatedAt"] = now.ToString("O");
        existing["authorId"] = actor.ToString();
        existing["message"] = "Updated by automation";
        var history = List(existing, "history");
        history.Insert(0, previous);
        if (history.Count > 20) history.RemoveAt(20);
        return new(true);
    }

    [GeneratedRegex("[^a-z0-9]+", RegexOptions.CultureInvariant)]
    private static partial Regex WikiSlug();

    private static Mutation Design(JsonObject document, ConnectionAction action)
    {
        var layers = List(document, "layers");
        if (action.Kind is "create-screen" or "create-layer")
        {
            var kind = action.Kind == "create-screen" ? "frame" : Arg(action, "kind");
            var id = NewId();
            var parent = action.Target.Kind == "ui-screen" ? Find(layers, action.Target.Id) : null;
            if (parent is not null && Flag(parent, "locked")) throw new ArgumentException("The destination screen is locked.");
            var x = parent?["x"]?.GetValue<double>() + 24 ?? (layers.Count % 5) * 460;
            var y = parent?["y"]?.GetValue<double>() + 24 ?? (layers.Count / 5) * 900;
            var layer = new JsonObject { ["id"] = id, ["kind"] = kind, ["name"] = Arg(action, "name"), ["x"] = x, ["y"] = y,
                ["width"] = kind == "frame" ? 390 : kind == "text" ? 240 : 180, ["height"] = kind == "frame" ? 844 : kind is "text" or "button" ? 48 : 120,
                ["fill"] = kind == "frame" ? "#ffffff" : kind == "button" ? "#7561df" : "#ddd6fe", ["stroke"] = "#dce0e8", ["strokeWidth"] = 0,
                ["radius"] = kind == "button" ? 10 : kind == "rectangle" ? 8 : 0, ["opacity"] = 1,
                ["text"] = Optional(action, "text") ?? "", ["fontSize"] = kind == "text" ? 24 : 14, ["fontFamily"] = "Geist Variable",
                ["textColor"] = kind == "button" ? "#ffffff" : "#202332", ["hidden"] = false, ["locked"] = false };
            if (parent is not null) layer["parentId"] = action.Target.Id;
            layers.Add(layer);
            if (kind == "frame" && document["startFrameId"] is null) document["startFrameId"] = id;
            return new(true, new(kind == "frame" ? "ui-screen" : "ui-layer", id));
        }
        var target = Find(layers, action.Target.Id);
        var frameId = Text(target, "parentId");
        if (frameId is not null && Flag(Find(layers, frameId), "locked")) throw new ArgumentException("The parent screen is locked.");
        if (Flag(target, "locked") && action.Kind is not ("show" or "hide" or "unlock" or "lock")) throw new ArgumentException("The selected design item is locked.");
        if (action.Kind is "duplicate-screen" or "duplicate-layer") return DuplicateDesign(layers, target, action.Target);
        if (action.Kind == "delete")
        {
            var removed = layers.OfType<JsonObject>().Where(layer => Text(layer, "parentId") == action.Target.Id)
                .Select(layer => Text(layer, "id")).Append(action.Target.Id).ToHashSet();
            RemoveWhere(layers, layer => removed.Contains(Text(layer, "id")));
            foreach (var layer in layers.OfType<JsonObject>().Where(layer => layer["interaction"] is JsonObject interaction && removed.Contains(Text(interaction, "targetId"))))
                layer.Remove("interaction");
            if (removed.Contains(Text(document, "startFrameId"))) document.Remove("startFrameId");
            return new(true);
        }
        return action.Kind switch
        {
            "rename" => new(Set(target, "name", Arg(action, "name"))), "set-text" => new(Set(target, "text", Optional(action, "text") ?? "")),
            "set-start" => new(Set(document, "startFrameId", action.Target.Id)), "hide" => new(Set(target, "hidden", true)),
            "show" => new(Set(target, "hidden", false)), "lock" => new(Set(target, "locked", true)), "unlock" => new(Set(target, "locked", false)),
            "set-layout" => new(SetDesignLayout(layers, target, action)),
            "set-style" => new(SetOptionalDesignColors(target, action)),
            "move-layer" => new(MoveDesignLayer(layers, target, BlankToNull(Optional(action, "parentId")))),
            "configure-interaction" => new(Set(target, "interaction", NewDesignInteraction(action))),
            "clear-interaction" => new(target.Remove("interaction")),
            _ => throw new ArgumentException("Choose a design action.")
        };
    }

    private static Mutation DuplicateDesign(JsonArray layers, JsonObject target, ConnectionNodeRef reference)
    {
        var group = layers.OfType<JsonObject>().Where(layer => Text(layer, "parentId") == reference.Id).Prepend(target).ToArray();
        var ids = group.ToDictionary(layer => Text(layer, "id")!, _ => NewId());
        foreach (var source in group)
        {
            var copy = source.DeepClone().AsObject();
            copy["id"] = ids[Text(source, "id")!];
            var name = Text(source, "name")!;
            copy["name"] = $"{name[..Math.Min(144, name.Length)]} copy";
            copy["x"] = Math.Min(1_000_000, source["x"]!.GetValue<double>() + 32);
            copy["y"] = Math.Min(1_000_000, source["y"]!.GetValue<double>() + 32);
            copy["locked"] = false;
            if (Text(source, "parentId") is { } previousParent && ids.TryGetValue(previousParent, out var parent)) copy["parentId"] = parent;
            if (copy["interaction"] is JsonObject interaction && Text(interaction, "targetId") is { } previousTarget && ids.TryGetValue(previousTarget, out var targetId)) interaction["targetId"] = targetId;
            layers.Add(copy);
        }
        return new(true, new(reference.Kind, ids[reference.Id!]));
    }

    private static bool SetOptionalDesignColors(JsonObject item, ConnectionAction action)
    {
        var changed = false;
        foreach (var field in new[] { "fill", "stroke", "textColor" })
            if (BlankToNull(Optional(action, field)) is { } value) changed |= Set(item, field, value);
        return changed;
    }

    private static bool SetDesignLayout(JsonArray layers, JsonObject screen, ConnectionAction action)
    {
        var layout = Arg(action, "layout");
        var changed = Set(screen, "layout", layout);
        foreach (var field in new[] { "gap", "padding" })
            if (BlankToNull(Optional(action, field)) is { } text)
            {
                if (!int.TryParse(text, out var value) || value is < 0 or > 500) throw new ArgumentException("Gap and padding must be 0–500 pixels.");
                changed |= Set(screen, field, value);
            }
        if (changed) LayoutDesignChildren(layers, screen);
        return changed;
    }

    private static void LayoutDesignChildren(JsonArray layers, JsonObject screen)
    {
        if (Text(screen, "layout") is not ("vertical" or "horizontal")) return;
        var vertical = Text(screen, "layout") == "vertical";
        var padding = screen["padding"] is JsonValue paddingValue ? Number(paddingValue) : 24;
        var gap = screen["gap"] is JsonValue gapValue ? Number(gapValue) : 16;
        var cursor = padding;
        foreach (var layer in layers.OfType<JsonObject>().Where(layer => Text(layer, "parentId") == Text(screen, "id") && !Flag(layer, "hidden")))
        {
            layer["x"] = Number(screen["x"]) + (vertical ? padding : cursor);
            layer["y"] = Number(screen["y"]) + (vertical ? cursor : padding);
            cursor += Number(layer[vertical ? "height" : "width"]) + gap;
        }
    }

    private static double Number(JsonNode? value) => value is JsonValue number && number.TryGetValue<double>(out var decimalValue) ? decimalValue : value!.GetValue<int>();

    private static bool MoveDesignLayer(JsonArray layers, JsonObject item, string? parentId)
    {
        if (parentId is null) return item.Remove("parentId");
        var frame = Find(layers, parentId);
        if (Text(frame, "kind") != "frame" || Flag(frame, "locked")) throw new ArgumentException("Choose an unlocked screen.");
        var changed = Set(item, "parentId", parentId);
        if (changed) LayoutDesignChildren(layers, frame);
        return changed;
    }

    private static JsonObject NewDesignInteraction(ConnectionAction action)
    {
        var kind = Arg(action, "interaction");
        var interaction = new JsonObject { ["action"] = kind, ["transition"] = BlankToNull(Optional(action, "transition")) ?? "instant" };
        if (kind is "navigate" or "overlay") interaction["targetId"] = Arg(action, "targetId");
        return interaction;
    }

    private static Mutation Uml(JsonObject document, ConnectionAction action, DateTimeOffset now)
    {
        var diagrams = List(document, "diagrams");
        if (action.Kind == "create-diagram")
        {
            var id = NewId();
            diagrams.Add(new JsonObject { ["id"] = id, ["name"] = Arg(action, "name"), ["type"] = BlankToNull(Optional(action, "type")),
                ["items"] = new JsonArray(), ["updatedAt"] = now.ToString("O") });
            return new(true, new("uml-diagram", id));
        }
        var diagram = Find(diagrams, action.Target.ParentId ?? action.Target.Id);
        if (action.Target.Kind == "uml-diagram" && action.Kind == "delete") return new(diagrams.Remove(diagram));
        if (action.Target.Kind == "uml-diagram" && action.Kind == "duplicate-diagram") return DuplicateUmlDiagram(diagrams, diagram, now);
        var items = List(diagram, "items");
        if (action.Target.Kind == "uml-diagram")
        {
            if (action.Kind is "add-element" or "add-uml-relationship")
            {
                var symbol = Arg(action, "symbol");
                var relation = action.Kind == "add-uml-relationship";
                var added = NewUmlItem(symbol, Optional(action, "text") ?? (relation ? "" : symbol), items.Count, relation);
                if (relation) added["connection"] = NewUmlConnection(Find(items, Arg(action, "startId")), Find(items, Arg(action, "endId")));
                items.Add(added);
                diagram["updatedAt"] = now.ToString("O");
                return new(true, new(relation ? "uml-relationship" : "uml-element", Text(added, "id"), action.Target.Id));
            }
            var changed = action.Kind == "set-diagram-type" ? Set(diagram, "type", BlankToNull(Optional(action, "type"))) : Set(diagram, "name", Arg(action, "name"));
            if (changed) diagram["updatedAt"] = now.ToString("O");
            return new(changed);
        }
        var item = Find(items, action.Target.Id);
        if (Flag(item, "locked") && action.Kind is not ("show" or "hide" or "unlock" or "lock")) throw new ArgumentException("The selected UML item is locked.");
        var edited = action.Kind switch
        {
            "set-text" => Set(item, "text", Optional(action, "text") ?? ""), "hide" => Set(item, "hidden", true), "show" => Set(item, "hidden", false),
            "lock" => Set(item, "locked", true), "unlock" => Set(item, "locked", false), "delete" => items.Remove(item),
            "set-relation-type" => Set(item, "umlSymbol", Arg(action, "symbol")),
            "reattach" => ReattachUml(item, Find(items, Arg(action, "startId")), Find(items, Arg(action, "endId"))),
            "reverse-relation" => ReverseUml(item),
            "set-guard" => Set(UmlConnection(item), "guard", Optional(action, "guard") ?? ""),
            "set-effect" => Set(UmlConnection(item), "effect", Optional(action, "effect") ?? ""),
            "set-relationship-details" => SetOptionalUmlDetails(UmlConnection(item), action, ["sourceRole", "targetRole", "sourceMultiplicity", "targetMultiplicity"]),
            "set-uml-details" => SetUmlElementDetails(item, action),
            _ => throw new ArgumentException("Choose a UML action.")
        };
        if (action.Kind == "delete") RemoveWhere(items, relation => relation["connection"] is JsonObject connection &&
            (Text(connection["start"], "shapeId") == action.Target.Id || Text(connection["end"], "shapeId") == action.Target.Id));
        if (edited) diagram["updatedAt"] = now.ToString("O");
        return new(edited);
    }

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static Mutation DuplicateUmlDiagram(JsonArray diagrams, JsonObject source, DateTimeOffset now)
    {
        var diagram = source.DeepClone().AsObject();
        var id = NewId();
        var name = Text(source, "name")!;
        diagram["id"] = id;
        diagram["name"] = $"{name[..Math.Min(name.Length, 144)]} copy";
        diagram["updatedAt"] = now.ToString("O");
        var items = List(diagram, "items").OfType<JsonObject>().ToArray();
        var replacements = items.ToDictionary(item => Text(item, "id")!, _ => NewId());
        var groups = items.Select(item => Text(item, "groupId")).OfType<string>().Distinct().ToDictionary(group => group, _ => NewId());
        foreach (var item in items)
        {
            item["id"] = replacements[Text(item, "id")!];
            if (Text(item, "groupId") is { } group) item["groupId"] = groups[group];
            if (item["connection"] is not JsonObject relation) continue;
            foreach (var end in new[] { "start", "end" })
                if (relation[end] is JsonObject endpoint && Text(endpoint, "shapeId") is { } previous) endpoint["shapeId"] = replacements[previous];
        }
        diagrams.Add(diagram);
        return new(true, new("uml-diagram", id));
    }

    private static bool SetOptionalUmlDetails(JsonObject destination, ConnectionAction action, IReadOnlyList<string> keys)
    {
        var changed = false;
        foreach (var key in keys)
            if (Optional(action, key) is { } value) changed |= Set(destination, key, value);
        return changed;
    }

    private static bool SetUmlElementDetails(JsonObject item, ConnectionAction action)
    {
        string[] keys = ["name", "stereotype", "attributes", "operations", "entry", "activity", "exit", "operator", "operands"];
        if (item["umlDetails"] is JsonObject existing) return SetOptionalUmlDetails(existing, action, keys);
        var details = new JsonObject(keys.Select(key => new KeyValuePair<string, JsonNode?>(key, JsonValue.Create(""))));
        if (!SetOptionalUmlDetails(details, action, keys)) return false;
        item["umlDetails"] = details;
        return true;
    }

    private static JsonObject NewUmlItem(string symbol, string label, int place, bool relation) => new()
    {
        ["id"] = NewId(), ["umlSymbol"] = symbol, ["kind"] = relation ? "arrow" : "rectangle", ["text"] = label,
        ["x"] = 48 + place % 6 * 245, ["y"] = 48 + place / 6 * 160, ["width"] = 200, ["height"] = relation ? 48 : 110,
        ["appearance"] = "outlined", ["fillColor"] = "#ffffff", ["outlineColor"] = "#312e81", ["outlineStyle"] = "solid",
        ["cornerRadius"] = 0, ["outlineWidth"] = 2, ["fontFamily"] = "Geist", ["fontSize"] = 14,
        ["textColor"] = "auto", ["textAlign"] = "left", ["verticalAlign"] = "top", ["bold"] = false, ["italic"] = false,
        ["underline"] = false, ["strikethrough"] = false, ["rotation"] = 0, ["flipHorizontal"] = false, ["flipVertical"] = false,
        ["letterSpacing"] = 0, ["lineHeight"] = 1.2, ["textIndent"] = 0
    };

    private static JsonObject UmlEndpoint(JsonObject item, string side) => new() { ["shapeId"] = Text(item, "id"), ["side"] = side, ["offset"] = 0.5 };

    private static JsonObject NewUmlConnection(JsonObject from, JsonObject to) => new()
    {
        ["start"] = UmlEndpoint(from, "right"), ["end"] = UmlEndpoint(to, "left"), ["routing"] = "straight", ["bends"] = new JsonArray(),
        ["sourceMultiplicity"] = "", ["targetMultiplicity"] = "", ["sourceRole"] = "", ["targetRole"] = "", ["guard"] = "", ["number"] = null
    };

    private static bool ReattachUml(JsonObject item, JsonObject from, JsonObject to)
    {
        if (item["connection"] is not JsonObject connection) return Set(item, "connection", NewUmlConnection(from, to));
        return Set(connection, "start", UmlEndpoint(from, "right")) | Set(connection, "end", UmlEndpoint(to, "left"));
    }

    private static bool ReverseUml(JsonObject item)
    {
        var connection = UmlConnection(item);
        foreach (var (start, end) in new[] { ("start", "end"), ("sourceMultiplicity", "targetMultiplicity"), ("sourceRole", "targetRole") })
        {
            var previous = connection[start]?.DeepClone();
            connection[start] = connection[end]?.DeepClone();
            connection[end] = previous;
        }
        if (connection["bends"] is JsonArray bends) connection["bends"] = new JsonArray(bends.Reverse().Select(bend => bend?.DeepClone()).ToArray());
        return true;
    }

    private static JsonObject UmlConnection(JsonObject item) => item["connection"] as JsonObject ?? throw new ArgumentException("Attach endpoints before editing the relationship.");
}
