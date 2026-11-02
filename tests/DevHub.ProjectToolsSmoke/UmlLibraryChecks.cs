using System.Text.Json;
using System.Text.Json.Nodes;
using DevHub.Infrastructure.ProjectTools;

internal static class UmlLibraryChecks
{
    public static string Example => File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures","uml-library.json"));
    public static void Run()
    {
        var original = JsonNode.Parse(Example)!;
        bool Valid(JsonNode node) => ToolDocumentValidation.Valid("uml",JsonSerializer.SerializeToElement(node));
        if (!Valid(original)) throw new Exception("Valid universal UML library rejected");
        foreach (var change in new Action<JsonNode>[] {
            d => d["version"] = 2, d => d["diagrams"]![0]!["name"] = " ", d => d["diagrams"]![0]!["type"] = "unknown",
            d => d["diagrams"]![1]!["id"] = "diagram-one", d => d["diagrams"]![0]!["updatedAt"] = "invalid",
            d => d["diagrams"]![0]!["items"]![0]!["umlSymbol"] = "unknown", d => d["diagrams"]![0]!["items"]![0]!["width"] = 0,
            d => d["diagrams"]![0]!["items"]![2]!["connection"]!["end"]!["shapeId"] = "missing",
            d => d["diagrams"]![0]!["items"]![2]!["connection"]!["end"]!["shapeId"] = "relationship-one",
            d => d["diagrams"]![0]!["items"]![2]!["connection"]!["start"]!["offset"] = 2,
            d => d["diagrams"]![0]!["items"]![2]!["connection"]!["routing"] = "unknown",
            d => d["diagrams"]![0]!["items"]![2]!["connection"]!["number"] = 1.5,
            d => d["diagrams"]![0]!["items"]![2]!["connection"]!["effect"] = 42,
            d => d["diagrams"]![0]!["items"]![0]!["locked"] = "false",
            d => d["diagrams"]![0]!["items"]![0]!["umlDetails"]!["name"] = new string('x',151),
            d => {var item=d["diagrams"]![0]!["items"]![1]!.DeepClone();d["diagrams"]![0]!["items"]!.AsArray().RemoveAt(1);d["diagrams"]![1]!["items"]!.AsArray().Add(item);}
        }) {var copy=original.DeepClone(); change(copy); if (Valid(copy)) throw new Exception("Malformed universal UML library accepted");}
        Console.WriteLine("PASS: Universal UML validates element details, attached relationships, endpoints, routing, and diagram boundaries");
    }
}
