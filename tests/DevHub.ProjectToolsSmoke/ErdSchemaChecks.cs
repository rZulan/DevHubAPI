using System.Text.Json;
using System.Text.Json.Nodes;
using DevHub.Infrastructure.ProjectTools;

internal static class ErdSchemaChecks
{
    public static void Run()
    {
        var document = JsonNode.Parse("""
        {"dialect":"mysql","tables":[
          {"id":"p","name":"parent","x":0,"y":0,"fields":[
            {"id":"tenant","name":"tenant","type":"INT","length":"","primary":true,"nullable":false,"unique":false,"defaultValue":""},
            {"id":"id","name":"id","type":"INT","length":"","primary":true,"nullable":false,"unique":false,"defaultValue":""}]},
          {"id":"c","name":"child","x":400,"y":0,"fields":[
            {"id":"id","name":"id","type":"INT","length":"","primary":true,"nullable":false,"unique":false,"defaultValue":"","autoIncrement":true},
            {"id":"tenant","name":"tenant","type":"INT","length":"","primary":false,"nullable":false,"unique":false,"defaultValue":""},
            {"id":"parent_id","name":"parent_id","type":"INT","length":"","primary":false,"nullable":false,"unique":false,"defaultValue":""}],
           "indexes":[{"id":"idx","name":"idx_child","fields":["tenant","parent_id"],"unique":false}],
           "checks":[{"id":"check","name":"positive_id","expression":"id > 0"}]}],
         "relationships":[{"id":"r","fromTable":"p","fromField":"tenant","fromFields":["tenant","id"],"toTable":"c","toField":"tenant","toFields":["tenant","parent_id"],"cardinality":"1:N","onDelete":"CASCADE"}]}
        """)!;
        static bool Valid(JsonNode node) => ToolDocumentValidation.Valid("erd", JsonSerializer.SerializeToElement(node));
        static void Check(bool condition, string name) {if (!condition) throw new Exception(name); Console.WriteLine("PASS: " + name);}
        foreach (var dialect in new[] {"postgresql", "mysql", "sqlserver", "sqlite"}) {document["dialect"] = dialect; Check(Valid(document), $"{dialect} accepts composite relationships, indexes, checks and generated IDs");}
        var invalid = document.DeepClone(); invalid["dialect"] = "unknown";
        Check(!Valid(invalid), "Unknown SQL target rejected");
        invalid = document.DeepClone(); invalid["relationships"]![0]!.AsObject().Remove("fromFields"); invalid["relationships"]![0]!.AsObject().Remove("toFields");
        Check(!Valid(invalid), "A partial composite primary key cannot be referenced");
        invalid = document.DeepClone(); invalid["relationships"]![0]!["toFields"] = new JsonArray("tenant", "missing");
        Check(!Valid(invalid), "Missing composite foreign key columns rejected");
        invalid = document.DeepClone(); invalid["relationships"]![0]!["toFields"] = new JsonArray("tenant", "tenant");
        Check(!Valid(invalid), "Repeated composite columns rejected");
        invalid = document.DeepClone(); invalid["tables"]![1]!["indexes"]![0]!["fields"] = new JsonArray();
        Check(!Valid(invalid), "Empty index columns rejected");
        invalid = document.DeepClone(); invalid["tables"]![1]!["indexes"]![0]!["fields"] = new JsonArray("missing");
        Check(!Valid(invalid), "Dangling index columns rejected");
        invalid = document.DeepClone(); invalid["tables"]![1]!["checks"]![0]!["expression"] = "";
        Check(!Valid(invalid), "Empty check expressions rejected");
        invalid = document.DeepClone(); invalid["tables"]![1]!["fields"]![0]!["autoIncrement"] = "true";
        Check(!Valid(invalid), "Malformed ID generation rejected");
        invalid = document.DeepClone(); invalid["relationships"]![0]!["onDelete"] = "SET NULL";
        Check(!Valid(invalid), "SET NULL checks every component of a composite foreign key");
        invalid = document.DeepClone(); invalid["tables"]![0]!["fields"]![0]!["primary"] = false; invalid["tables"]![0]!["fields"]![1]!["primary"] = false;
        invalid["tables"]![0]!["indexes"] = new JsonArray(JsonNode.Parse("""{"id":"u","name":"uq_parent","fields":["tenant","id"],"unique":true}"""));
        Check(Valid(invalid), "Composite unique indexes can be referenced");
        invalid["relationships"]![0]!["fromAnchor"] = JsonNode.Parse("""{"edge":"top","offset":2}""");
        Check(!Valid(invalid), "Out of range relationship anchors rejected");
    }
}
