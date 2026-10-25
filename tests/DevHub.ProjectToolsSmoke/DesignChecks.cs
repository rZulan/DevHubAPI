using System.Text.Json;
using System.Text.Json.Nodes;
using DevHub.Infrastructure.ProjectTools;

internal static class DesignChecks
{
    public static void Run()
    {
        var document = JsonNode.Parse("""
        {"startFrameId":"screen","layers":[
            {"id":"screen","name":"Welcome","kind":"frame","x":100,"y":100,"width":390,"height":844,"fill":"#ffffff","stroke":"#dce0e8","strokeWidth":0,"radius":12,"opacity":1,"text":"","fontSize":16,"textColor":"#202332","hidden":false,"locked":false,"layout":"vertical","gap":16,"padding":24},
            {"id":"action","name":"Continue","kind":"button","parentId":"screen","x":124,"y":200,"width":220,"height":48,"fill":"#7561df","stroke":"#dce0e8","strokeWidth":0,"radius":10,"opacity":1,"text":"Continue","fontSize":14,"textColor":"#ffffff","hidden":false,"locked":false,"fontFamily":"Geist Variable","fontWeight":600,"textAlign":"center","lineHeight":1.4,"shadow":true,"interaction":{"action":"navigate","targetId":"screen","transition":"fade"}}
        ]}
        """)!;
        bool Valid(JsonNode node) => ToolDocumentValidation.Valid("ui-editor", JsonSerializer.SerializeToElement(node));
        void Check(bool condition, string message) {if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message);}
        Check(Valid(document), "Design layout, typography and prototype metadata accepted");
        foreach (var (property, value) in new (string, JsonNode?)[] {("fontWeight", JsonValue.Create(999)), ("lineHeight", JsonValue.Create(0)), ("layout", JsonValue.Create("grid")), ("control", JsonValue.Create("script")), ("image", JsonValue.Create("https://external.test/image.png")), ("imageFit", JsonValue.Create("invalid"))})
        {
            var invalid = document.DeepClone(); invalid["layers"]![1]![property] = value;
            Check(!Valid(invalid), $"Invalid design {property} rejected");
        }
        foreach (var action in new[] {"navigate", "overlay", "back", "close"})
        {
            var next = document.DeepClone(); var interaction = next["layers"]![1]!["interaction"]!;
            interaction["action"] = action;
            if (action is "back" or "close") interaction.AsObject().Remove("targetId");
            Check(Valid(next), $"Prototype {action} action accepted");
        }
        var missing = document.DeepClone(); missing["layers"]![1]!["interaction"]!["targetId"] = "missing";
        Check(!Valid(missing), "Dangling prototype destination rejected");
        var start = document.DeepClone(); start["startFrameId"] = "action";
        Check(!Valid(start), "Starting point must refer to a frame");
        foreach (var control in new[] {"input", "checkbox", "toggle"})
        {
            var next = document.DeepClone(); next["layers"]![1]!["control"] = control;
            Check(Valid(next), $"Interactive {control} control accepted");
        }
        var legacy = document.DeepClone(); legacy.AsObject().Remove("startFrameId");
        foreach (var layer in legacy["layers"]!.AsArray()) foreach (var field in new[] {"layout", "gap", "padding", "fontFamily", "fontWeight", "textAlign", "lineHeight", "shadow", "interaction"}) layer!.AsObject().Remove(field);
        Check(Valid(legacy), "Existing design documents remain valid");
    }
}
