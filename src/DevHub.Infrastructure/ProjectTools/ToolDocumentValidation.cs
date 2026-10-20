using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevHub.Infrastructure.ProjectTools;

public static class ToolDocumentValidation
{
    private static string? Text(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool String(JsonElement item, string name, int max, bool required = false) => Text(item, name) is { } text && text.Length <= max && (!required || !string.IsNullOrWhiteSpace(text));
    private static bool Number(JsonElement item, string name, double min, double max) => item.TryGetProperty(name, out var value) && value.TryGetDouble(out var n) && double.IsFinite(n) && n >= min && n <= max;
    private static bool Boolean(JsonElement item, string name) => item.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False;
    private static bool Array(JsonElement item, string name, int max, out JsonElement value) => item.TryGetProperty(name, out value) && value.ValueKind == JsonValueKind.Array && value.GetArrayLength() <= max;
    private static bool Color(JsonElement item, string name) => Text(item, name) is { } color && Regex.IsMatch(color, "^#[a-fA-F0-9]{6}$");
    private static bool Identity(JsonElement item, HashSet<string> ids) => String(item, "id", 100, true) && ids.Add(Text(item, "id")!);
    public static bool Valid(string tool, JsonElement document)
    {
        if (document.ValueKind != JsonValueKind.Object || document.GetRawText().Length > 4_000_000) return false;
        try { return tool switch { "erd" => Erd(document), "ui-editor" => Design(document), "sdlc" => Roadmap(document), "notes" => Notes(document), _ => false }; }
        catch (InvalidOperationException) { return false; }
    }
    private static bool Erd(JsonElement document)
    {
        if (document.TryGetProperty("dialect", out _) && Text(document, "dialect") is not ("postgresql" or "mysql" or "sqlserver" or "sqlite")) return false;
        if (!Array(document, "tables", 500, out var tables) || !Array(document, "relationships", 2000, out var relationships)) return false;
        var ids = new HashSet<string>();
        var tableFields = new Dictionary<string, Dictionary<string, JsonElement>>();
        var definitions = new Dictionary<string, JsonElement>();
        foreach (var table in tables.EnumerateArray())
        {
            if (table.ValueKind != JsonValueKind.Object || !Identity(table, ids) || !String(table, "name", 150, true) || !Number(table, "x", -1_000_000, 1_000_000) || !Number(table, "y", -1_000_000, 1_000_000) || !Array(table, "fields", 200, out var fields)) return false;
            var fieldIds = new HashSet<string>();
            var byId = new Dictionary<string, JsonElement>();
            foreach (var field in fields.EnumerateArray())
            {
                if (field.ValueKind != JsonValueKind.Object || !Identity(field, fieldIds) || !String(field, "name", 150, true) || Text(field, "type") is not ("UUID" or "INT" or "BIGINT" or "VARCHAR" or "TEXT" or "BOOLEAN" or "DATE" or "TIMESTAMP" or "DECIMAL" or "FLOAT" or "JSON" or "BLOB") || !String(field, "length", 30) || !String(field, "defaultValue", 500) || !Boolean(field, "primary") || !Boolean(field, "nullable") || !Boolean(field, "unique")) return false;
                if (field.TryGetProperty("autoIncrement", out _) && !Boolean(field, "autoIncrement")) return false;
                byId.Add(Text(field, "id")!, field);
            }
            var constraintIds = new HashSet<string>();
            if (table.TryGetProperty("indexes", out _))
            {
                if (!Array(table, "indexes", 200, out var indexes)) return false;
                foreach (var index in indexes.EnumerateArray())
                    if (index.ValueKind != JsonValueKind.Object || !Identity(index, constraintIds) || !String(index, "name", 150, true) || !Boolean(index, "unique") || !ColumnList(index, "fields", out var columns) || columns.Any(id => !byId.ContainsKey(id))) return false;
            }
            if (table.TryGetProperty("checks", out _))
            {
                if (!Array(table, "checks", 200, out var checks)) return false;
                foreach (var check in checks.EnumerateArray())
                    if (check.ValueKind != JsonValueKind.Object || !Identity(check, constraintIds) || !String(check, "name", 150, true) || !String(check, "expression", 2000, true)) return false;
            }
            tableFields.Add(Text(table, "id")!, byId);
            definitions.Add(Text(table, "id")!, table);
        }
        var relationIds = new HashSet<string>();
        foreach (var relation in relationships.EnumerateArray())
        {
            if (relation.ValueKind != JsonValueKind.Object || !Identity(relation, relationIds) || Text(relation, "cardinality") is not ("1:1" or "1:N" or "N:1" or "N:M") || Text(relation, "onDelete") is not ("RESTRICT" or "CASCADE" or "SET NULL")) return false;
            foreach (var end in new[] { "leftEnd", "rightEnd" })
                if (relation.TryGetProperty(end, out _) && Text(relation, end) is not ("one" or "zero-one" or "many" or "zero-many")) return false;
            var cardinality = Text(relation, "cardinality");
            var leftMany = (Text(relation, "leftEnd") ?? (cardinality is "N:1" or "N:M" ? "many" : "one")) is "many" or "zero-many";
            var rightMany = (Text(relation, "rightEnd") ?? (cardinality is "1:N" or "N:M" ? "many" : "one")) is "many" or "zero-many";
            if (cardinality != (leftMany ? rightMany ? "N:M" : "N:1" : rightMany ? "1:N" : "1:1")) return false;
            if (Text(relation, "fromTable") is not { } from || Text(relation, "toTable") is not { } to || Text(relation, "fromField") is not { } fromField || Text(relation, "toField") is not { } toField || !tableFields.TryGetValue(from, out var source) || !tableFields.TryGetValue(to, out var target) || !source.TryGetValue(fromField, out var parent) || !target.TryGetValue(toField, out var child)) return false;
            var fromIds = new[] {fromField}; var toIds = new[] {toField};
            if (relation.TryGetProperty("fromFields", out _) && !ColumnList(relation, "fromFields", out fromIds) || relation.TryGetProperty("toFields", out _) && !ColumnList(relation, "toFields", out toIds)) return false;
            if (fromIds[0] != fromField || toIds[0] != toField || fromIds.Any(id => !source.ContainsKey(id)) || toIds.Any(id => !target.ContainsKey(id))) return false;
            if (from == to && fromIds.Where((id, i) => i < toIds.Length && id == toIds[i]).Any()) return false;
            foreach (var anchorName in new[] {"fromAnchor", "toAnchor"})
                if (relation.TryGetProperty(anchorName, out var anchor) && (anchor.ValueKind != JsonValueKind.Object || Text(anchor, "edge") is not ("left" or "right" or "top" or "bottom") || !Number(anchor, "offset", 0, 1))) return false;
            if (cardinality == "N:M") continue;
            if (fromIds.Length != toIds.Length) return false;
            var parentIds = cardinality == "N:1" ? toIds : fromIds;
            var parents = (cardinality == "N:1" ? target : source);
            var children = (cardinality == "N:1" ? source : target);
            var childIds = cardinality == "N:1" ? fromIds : toIds;
            if (!ErdKey(definitions[cardinality == "N:1" ? to : from], parents, parentIds)) return false;
            for (var i = 0; i < parentIds.Length; i++)
            {
                parent = parents[parentIds[i]]; child = children[childIds[i]];
                if (Text(parent, "type") != Text(child, "type") || ErdLength(parent) != ErdLength(child)) return false;
                if (Text(relation, "onDelete") == "SET NULL" && (!child.GetProperty("nullable").GetBoolean() || child.GetProperty("primary").GetBoolean())) return false;
            }
        }
        return true;
    }
    private static bool ColumnList(JsonElement item, string name, out string[] columns)
    {
        columns = [];
        if (!Array(item, name, 200, out var values) || values.GetArrayLength() == 0 || values.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 100)) return false;
        columns = values.EnumerateArray().Select(value => value.GetString()!).ToArray();
        return columns.Distinct().Count() == columns.Length;
    }
    private static string ErdLength(JsonElement field)
    {
        var length = Regex.Replace(Text(field, "length") ?? "", @"\s", "");
        return Text(field, "type") switch {"VARCHAR" => length.Length == 0 ? "255" : length, "DECIMAL" => length.Length == 0 ? "10,2" : length.Contains(',') ? length : length + ",0", _ => ""};
    }
    private static bool ErdKey(JsonElement table, Dictionary<string, JsonElement> fields, string[] columns)
    {
        bool Same(IEnumerable<string> ids) {var key = ids.ToArray(); return key.Length == columns.Length && key.All(columns.Contains);}
        if (Same(fields.Where(pair => pair.Value.GetProperty("primary").GetBoolean()).Select(pair => pair.Key)) || columns.Length == 1 && fields[columns[0]].GetProperty("unique").GetBoolean()) return true;
        return table.TryGetProperty("indexes", out var indexes) && indexes.EnumerateArray().Any(index => index.GetProperty("unique").GetBoolean() && Same(index.GetProperty("fields").EnumerateArray().Select(value => value.GetString()!)));
    }
    private static bool Design(JsonElement document)
    {
        if (!Array(document, "layers", 5000, out var layers)) return false;
        var ids = new HashSet<string>();
        var frames = layers.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.Object && Text(l, "kind") == "frame").Select(l => Text(l, "id")).ToHashSet();
        foreach (var layer in layers.EnumerateArray())
        {
            if (layer.ValueKind != JsonValueKind.Object || !Identity(layer, ids) || !String(layer, "name", 150, true) || Text(layer, "kind") is not ("frame" or "rectangle" or "ellipse" or "text" or "button") || !Number(layer, "x", -1_000_000, 1_000_000) || !Number(layer, "y", -1_000_000, 1_000_000) || !Number(layer, "width", 16, 10000) || !Number(layer, "height", 16, 10000) || !Number(layer, "radius", 0, 5000) || !Number(layer, "strokeWidth", 0, 20) || !Number(layer, "opacity", 0, 1) || !Number(layer, "fontSize", 1, 500) || !String(layer, "text", 100000) || !Color(layer, "fill") || !Color(layer, "stroke") || !Color(layer, "textColor") || !Boolean(layer, "hidden") || !Boolean(layer, "locked")) return false;
            if (layer.TryGetProperty("parentId", out _) && (Text(layer, "parentId") is not { } parent || !frames.Contains(parent) || Text(layer, "kind") == "frame")) return false;
        }
        return true;
    }
    private static bool Notes(JsonElement document)
    {
        if (!Array(document, "notes", 1000, out var notes)) return false;
        var ids = new HashSet<string>();
        return notes.EnumerateArray().All(note => note.ValueKind == JsonValueKind.Object
            && Identity(note, ids) && String(note, "title", 150, true) && String(note, "body", 10000)
            && Color(note, "color") && String(note, "updatedAt", 100, true)
            && DateTimeOffset.TryParse(Text(note, "updatedAt"), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out _));
    }
    private static bool Roadmap(JsonElement document)
    {
        if (Text(document, "model") != "waterfall" || !Number(document, "currentPhase", 0, 5) || !document.GetProperty("currentPhase").TryGetInt32(out _) || !Boolean(document, "completed") || document.GetProperty("completed").GetBoolean() && document.GetProperty("currentPhase").GetInt32() != 5 || !document.TryGetProperty("notes", out var notes) || notes.ValueKind != JsonValueKind.Object) return false;
        var keys = new HashSet<string> {"requirements", "design", "implementation", "verification", "deployment", "maintenance"};
        return notes.EnumerateObject().All(p => keys.Contains(p.Name) && p.Value.ValueKind == JsonValueKind.String && (p.Value.GetString()?.Length ?? 0) <= 10000);
    }
}
