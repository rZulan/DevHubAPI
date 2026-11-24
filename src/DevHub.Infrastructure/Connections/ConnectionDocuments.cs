using System.Text.Json;
using DevHub.Application.Connections;

namespace DevHub.Infrastructure.Connections;

internal sealed record ConnectionSnapshot(ConnectionNodeRef Reference, string Label, string Path,
    IReadOnlyList<ConnectionNodeRef> Ancestors, IReadOnlyDictionary<string, string> Fields,
    IReadOnlyDictionary<string, JsonElement> Tracked);

internal static class ConnectionDocuments
{
    internal static readonly IReadOnlyDictionary<string, string> Tools = new Dictionary<string, string>
    {
        ["todo"] = "TODOs", ["erd"] = "ERD", ["calendar"] = "Calendar", ["bug-reports"] = "Bug Reports",
        ["ui-editor"] = "UI/UX Editor", ["sdlc"] = "SDLC Roadmap", ["uml"] = "UML", ["wiki"] = "Project Wiki", ["notes"] = "Notes"
    };

    internal static string? Text(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    internal static IEnumerable<JsonElement> Items(JsonElement item, string key) => item.ValueKind == JsonValueKind.Object &&
        item.TryGetProperty(key, out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray() : Enumerable.Empty<JsonElement>();

    private static ConnectionSnapshot Snapshot(string kind, string label, string path, JsonElement item,
        string? parentId, IReadOnlyList<ConnectionNodeRef> ancestors, string[] fields, string[] tracked) =>
        new(new(kind, Text(item, "id")!, parentId), label, path, ancestors,
            fields.Where(field => item.TryGetProperty(field, out _)).ToDictionary(field => field, field =>
                item.GetProperty(field).ValueKind == JsonValueKind.String ? item.GetProperty(field).GetString()! : item.GetProperty(field).GetRawText()),
            tracked.Where(field => item.TryGetProperty(field, out _)).ToDictionary(field => field, field => item.GetProperty(field)));

    internal static IEnumerable<ConnectionSnapshot> Snapshots(string tool, JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object) yield break;
        ConnectionNodeRef root = new(tool);
        string path = Tools[tool];
        switch (tool)
        {
            case "erd":
                var tables = Items(document, "tables").ToArray();
                var names = tables.ToDictionary(table => Text(table, "id")!, table => Text(table, "name")!);
                foreach (var table in tables)
                {
                    var name = Text(table, "name")!;
                    var parent = Snapshot("erd-table", name, $"{path} › {name}", table, null, [root], ["name"], ["name", "indexes", "checks"]);
                    yield return parent;
                    foreach (var column in Items(table, "fields"))
                    {
                        var columnName = Text(column, "name")!;
                        yield return Snapshot("erd-column", columnName, $"{parent.Path} › {columnName}", column,
                            parent.Reference.Id, [parent.Reference, root], ["name", "type", "nullable", "primary", "unique"],
                            ["name", "type", "length", "nullable", "primary", "unique", "defaultValue", "autoIncrement"]);
                    }
                }
                foreach (var relation in Items(document, "relationships"))
                {
                    var from = Text(relation, "fromTable")!;
                    var to = Text(relation, "toTable")!;
                    var label = $"{names.GetValueOrDefault(from, from)} → {names.GetValueOrDefault(to, to)}";
                    yield return Snapshot("erd-relationship", label, $"{path} › {label}", relation, null,
                        from == to ? [new("erd-table", from), root] : [new("erd-table", from), new("erd-table", to), root],
                        ["fromTable", "toTable", "cardinality", "onDelete"],
                        ["fromTable", "toTable", "fromField", "toField", "fromFields", "toFields", "cardinality", "onDelete", "leftEnd", "rightEnd"]);
                }
                break;
            case "calendar":
                foreach (var entry in Items(document, "events"))
                {
                    var label = Text(entry, "title")!;
                    yield return Snapshot("calendar-entry", label, $"{path} › {label}", entry, null, [root],
                        ["title", "kind", "startDate", "endDate", "startTime", "endTime", "allDay"],
                        ["title", "description", "kind", "startDate", "endDate", "startTime", "endTime", "allDay"]);
                }
                break;
            case "notes":
                foreach (var entry in Items(document, "notes"))
                {
                    var label = Text(entry, "title")!;
                    yield return Snapshot("sticky-note", label, $"{path} › {label}", entry, null, [root],
                        ["title", "color", "emoji"], ["title", "body", "color", "emoji"]);
                }
                break;
            case "wiki":
                var pages = Items(document, "pages").ToArray();
                var pageNames = pages.ToDictionary(page => Text(page, "id")!, page => Text(page, "title")!);
                var pageParents = pages.ToDictionary(page => Text(page, "id")!, page => Text(page, "parentId"));
                foreach (var page in pages)
                {
                    var label = Text(page, "title")!;
                    var ancestorIds = ParentChain(Text(page, "parentId"), pageParents);
                    var nodePath = string.Join(" › ", ancestorIds.Reverse().Select(id => pageNames[id]).Prepend(path).Append(label));
                    yield return Snapshot("wiki-page", label, nodePath, page, null,
                        [.. ancestorIds.Select(id => new ConnectionNodeRef("wiki-page", id)), root], ["title", "parentId", "slug"],
                        ["title", "body", "parentId", "message"]);
                }
                break;
            case "ui-editor":
                var layers = Items(document, "layers").ToArray();
                var layerNames = layers.ToDictionary(layer => Text(layer, "id")!, layer => Text(layer, "name")!);
                foreach (var layer in layers)
                {
                    var label = Text(layer, "name")!;
                    var frame = Text(layer, "parentId");
                    var kind = Text(layer, "kind") == "frame" ? "ui-screen" : "ui-layer";
                    var nodePath = frame is not null && layerNames.TryGetValue(frame, out var name) ? $"{path} › {name} › {label}" : $"{path} › {label}";
                    yield return Snapshot(kind, label, nodePath, layer, null,
                        frame is null ? [root] : [new("ui-screen", frame), root],
                        ["name", "kind", "hidden", "locked", "parentId"],
                        ["name", "text", "image", "control", "hidden", "locked", "parentId", "interaction", "fill", "stroke", "width", "height", "layout", "gap", "padding", "opacity", "fontSize"]);
                }
                break;
            case "uml":
                foreach (var diagram in Items(document, "diagrams"))
                {
                    var name = Text(diagram, "name")!;
                    var parent = Snapshot("uml-diagram", name, $"{path} › {name}", diagram, null, [root], ["name", "type"], ["name", "type"]);
                    yield return parent;
                    foreach (var item in Items(diagram, "items"))
                    {
                        var kind = Text(item, "kind")!;
                        var isRelationship = kind is "connection" or "arrow" || item.TryGetProperty("connection", out _);
                        var label = Text(item, "label") ?? Text(item, "text") ?? kind;
                        if (item.TryGetProperty("umlDetails", out var details) && Text(details, "name") is { Length: > 0 } detailName) label = detailName;
                        if (string.IsNullOrWhiteSpace(label)) label = kind;
                        var snapshot = Snapshot(isRelationship ? "uml-relationship" : "uml-element", label, $"{parent.Path} › {label}", item, parent.Reference.Id,
                            [parent.Reference, root], ["kind", "umlSymbol", "hidden", "locked"], ["kind", "text", "umlDetails", "umlSymbol", "hidden", "locked"]);
                        if (item.TryGetProperty("connection", out var connection))
                        {
                            var tracked = new Dictionary<string, JsonElement>(snapshot.Tracked);
                            foreach (var key in new[] { "sourceMultiplicity", "targetMultiplicity", "sourceRole", "targetRole", "guard", "effect", "number" })
                                if (connection.TryGetProperty(key, out var value)) tracked[key] = value;
                            tracked["startShape"] = JsonSerializer.SerializeToElement(Text(connection.GetProperty("start"), "shapeId"));
                            tracked["endShape"] = JsonSerializer.SerializeToElement(Text(connection.GetProperty("end"), "shapeId"));
                            snapshot = snapshot with { Tracked = tracked };
                        }
                        yield return snapshot;
                    }
                }
                break;
            case "sdlc":
                foreach (var node in SdlcConnections.Snapshots(document)) yield return node;
                break;
        }
    }

    private static IReadOnlyList<string> ParentChain(string? parent, IReadOnlyDictionary<string, string?> parents)
    {
        List<string> chain = [];
        while (parent is not null && !chain.Contains(parent) && parents.TryGetValue(parent, out var next))
        {
            chain.Add(parent);
            parent = next;
        }
        return chain;
    }

    private static string Prefix(string kind) => kind switch
    {
        "erd-table" => "table", "erd-column" => "column", "erd-relationship" => "relationship", "calendar-entry" => "entry",
        "sticky-note" => "note", "wiki-page" => "page", "ui-screen" => "screen", "ui-layer" => "layer", "uml-diagram" => "diagram",
        "uml-element" => "element", "uml-relationship" => "relationship", "sdlc-model" => "model", "sdlc-phase" => "phase",
        "sdlc-cycle" => "cycle", "sdlc-release" => "release", _ => kind
    };

    internal static IEnumerable<ConnectionEventDraft> Changes(string tool, JsonElement previous, JsonElement next)
    {
        var newRoadmap = tool == "sdlc" && Text(next, "roadmapId") is { } roadmapId && roadmapId != Text(previous, "roadmapId");
        var before = Snapshots(tool, newRoadmap ? default : previous).ToDictionary(item => item.Reference);
        var after = Snapshots(tool, next).ToDictionary(item => item.Reference);
        foreach (var (reference, item) in after)
        {
            var prefix = Prefix(reference.Kind);
            if (!before.TryGetValue(reference, out var existing))
            {
                yield return new(reference, $"{prefix}-added", item.Fields, item.Ancestors);
                continue;
            }
            var changed = existing.Tracked.Keys.Union(item.Tracked.Keys).Where(key =>
                !existing.Tracked.TryGetValue(key, out var old) || !item.Tracked.TryGetValue(key, out var updated) || !JsonElement.DeepEquals(old, updated)).ToArray();
            if (changed.Length == 0) continue;
            var fields = new Dictionary<string, string>(item.Fields) { ["changed"] = string.Join(",", changed) };
            var nameKey = fields.ContainsKey("name") ? "name" : "title";
            if (changed.Contains(nameKey) && existing.Fields.TryGetValue(nameKey, out var previousName))
            {
                fields["previousName"] = previousName;
                yield return new(reference, $"{prefix}-renamed", fields, item.Ancestors);
            }
            var triggers = existing.Ancestors.Concat(item.Ancestors).Distinct().ToArray();
            yield return new(reference, $"{prefix}-updated", fields, triggers);
            if (ConnectionCatalog.DetailedEvents.TryGetValue(reference.Kind, out var detailed))
                foreach (var detail in detailed.Where(detail => detail.Properties.Any(changed.Contains)))
                    yield return new(reference, detail.Kind, fields, triggers);
        }
        foreach (var (reference, item) in before)
            if (!after.ContainsKey(reference)) yield return new(reference, $"{Prefix(reference.Kind)}-removed", item.Fields, item.Ancestors);

        if (tool == "erd")
        {
            var previousNames = before.Values.Where(item => item.Reference.Kind == "erd-table").Select(item => item.Label.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var item in after.Values.Where(item => item.Reference.Kind == "erd-table"))
                if (previousNames.Add(item.Label.Trim())) yield return new(new("erd"), "table-name-appears",
                    new Dictionary<string, string>(item.Fields) { ["name"] = item.Label.Trim() }, []);
        }

        if (tool == "sdlc")
            foreach (var change in SdlcConnections.Events(previous, next, newRoadmap)) yield return change;

        var rootProperty = tool switch { "erd" => "dialect", "wiki" => "homePageId", "ui-editor" => "startFrameId", "calendar" => "timeZone", _ => null };
        if (rootProperty is not null && Text(previous, rootProperty) != Text(next, rootProperty))
        {
            var kind = tool switch { "erd" => "dialect-changed", "wiki" => "home-changed", "ui-editor" => "start-screen-changed", _ => "timezone-changed" };
            yield return new(new(tool), kind, new Dictionary<string, string> { [rootProperty] = Text(next, rootProperty) ?? "" }, []);
            if (tool is "wiki" or "ui-editor")
            {
                var nodeKind = tool == "wiki" ? "wiki-page" : "ui-screen";
                var suffix = tool == "wiki" ? "home" : "start-screen";
                if (Text(previous, rootProperty) is { } oldId && before.TryGetValue(new(nodeKind, oldId), out var old))
                    yield return new(old.Reference, $"stopped-being-{suffix}", old.Fields, old.Ancestors);
                if (Text(next, rootProperty) is { } newId && after.TryGetValue(new(nodeKind, newId), out var updated))
                    yield return new(updated.Reference, $"became-{suffix}", updated.Fields, updated.Ancestors);
            }
        }
    }
}
