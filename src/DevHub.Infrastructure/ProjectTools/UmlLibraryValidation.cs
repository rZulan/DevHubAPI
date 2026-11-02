using System.Text.Json;
using DevHub.Infrastructure.Ideas;

namespace DevHub.Infrastructure.ProjectTools;

public static class UmlLibraryValidation
{
    private static readonly HashSet<string> Types = ["class", "sequence", "use-case", "activity", "state-machine", "component", "deployment", "object", "package", "communication", "timing", "interaction-overview", "composite-structure", "profile"];
    private static readonly HashSet<string> Symbols = ["class", "interface", "enumeration", "actor", "use-case", "boundary", "lifeline", "activation", "fragment", "action", "initial", "final", "flow-final", "decision", "fork", "state", "history", "deep-history", "terminate", "component", "node", "object", "package", "port", "timing", "stereotype", "association", "directed", "generalization", "realization", "dependency", "aggregation", "composition", "include", "extend", "sync", "async", "reply", "note", "constraint", "provided-interface", "required-interface", "artifact", "junction", "entry-point", "exit-point", "send-signal", "accept-event", "time-event", "region", "destruction", "create-message", "destroy-message", "note-link", "extension", "containment"];
    private static readonly HashSet<string> Connectors = ["association", "directed", "generalization", "realization", "dependency", "aggregation", "composition", "include", "extend", "sync", "async", "reply", "create-message", "destroy-message", "note-link", "extension", "containment"];
    private static string? Text(JsonElement value, string key) => value.TryGetProperty(key,out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    public static bool Valid(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("version",out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1 || !value.TryGetProperty("diagrams",out var diagrams) || diagrams.ValueKind != JsonValueKind.Array || diagrams.GetArrayLength() > 100) return false;
        var ids = new HashSet<string>();
        foreach (var d in diagrams.EnumerateArray())
        {
            if (d.ValueKind != JsonValueKind.Object || Text(d,"id") is not {Length: > 0 and <= 100} id || string.IsNullOrWhiteSpace(id) || !ids.Add(id) || Text(d,"name") is not {Length: > 0 and <= 150} name || string.IsNullOrWhiteSpace(name) || Text(d,"updatedAt") is not {Length: <= 100} date || !DateTimeOffset.TryParse(date,out _) || !d.TryGetProperty("type",out var type) || !(type.ValueKind == JsonValueKind.Null || type.ValueKind == JsonValueKind.String && Types.Contains(type.GetString()!)) || !d.TryGetProperty("items",out var items) || !IdeasService.ValidItems(items)) return false;
            var index = items.EnumerateArray().ToDictionary(i => Text(i,"id")!);
            foreach (var item in items.EnumerateArray())
            {
                if (Text(item,"umlSymbol") is not { } symbol || !Symbols.Contains(symbol)) return false;
                foreach (var flag in new[] {"locked","hidden"})
                    if (item.TryGetProperty(flag,out var boolean) && boolean.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                if (item.TryGetProperty("umlDetails",out var details))
                {
                    if (details.ValueKind != JsonValueKind.Object) return false;
                    foreach (var key in new[] {"name","stereotype","attributes","operations","entry","activity","exit","operator","operands"})
                        if (Text(details,key) is not { } content || content.Length > (key is "attributes" or "operations" or "operands" ? 10000 : key == "name" ? 150 : 2000)) return false;
                }
                if (item.TryGetProperty("connection",out var connection) && (!Connectors.Contains(symbol) || !ValidConnection(connection,index,Text(item,"id")!))) return false;
            }
        }
        return true;
    }

    private static bool Number(JsonElement value,string key,double min,double max) => value.TryGetProperty(key,out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var n) && double.IsFinite(n) && n >= min && n <= max;
    private static bool Point(JsonElement value) => value.ValueKind == JsonValueKind.Object && !value.TryGetProperty("shapeId",out _) && Number(value,"x",-1e12,1e12) && Number(value,"y",-1e12,1e12);
    private static bool Endpoint(JsonElement value,Dictionary<string,JsonElement> items,string connectionId)
    {
        if (value.ValueKind != JsonValueKind.Object) return false;
        if (!value.TryGetProperty("shapeId",out _)) return Point(value);
        return Text(value,"shapeId") is {Length: > 0 and <= 100} id && id != connectionId && items.TryGetValue(id,out var target) && !target.TryGetProperty("connection",out _) && !Connectors.Contains(Text(target,"umlSymbol") ?? "") && Text(value,"side") is "top" or "right" or "bottom" or "left" && Number(value,"offset",0,1);
    }
    private static bool ValidConnection(JsonElement value,Dictionary<string,JsonElement> items,string id)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("start",out var start) || !Endpoint(start,items,id) || !value.TryGetProperty("end",out var end) || !Endpoint(end,items,id) || Text(value,"routing") is not ("straight" or "orthogonal" or "curve") || !value.TryGetProperty("bends",out var bends) || bends.ValueKind != JsonValueKind.Array || bends.GetArrayLength() > 32 || !bends.EnumerateArray().All(Point)) return false;
        foreach (var key in new[] {"sourceMultiplicity","targetMultiplicity","sourceRole","targetRole","guard"})
            if (Text(value,key) is not { } content || content.Length > (key == "guard" ? 500 : 100)) return false;
        if (value.TryGetProperty("effect",out _) && Text(value,"effect") is not {Length: <= 2000}) return false;
        return value.TryGetProperty("number",out var number) && (number.ValueKind == JsonValueKind.Null || number.ValueKind == JsonValueKind.Number && number.TryGetInt32(out var n) && n >= 1 && n <= 10000);
    }
}
