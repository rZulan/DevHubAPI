using System.Text.Json;
using DevHub.Infrastructure.ProjectTools;

internal static class RoadmapChecks
{
    public static void Run()
    {
        static void Check(string json, bool expected, string label)
        {
            if (ToolDocumentValidation.Valid("sdlc", JsonSerializer.Deserialize<JsonElement>(json)) != expected)
                throw new InvalidOperationException($"SDLC validation failed: {label}");
        }
        Check("""{"model":"waterfall","currentPhase":2,"completed":false,"notes":{"implementation":"Existing work"}}""", true, "legacy Waterfall");
        Check("""{"model":"v-model","currentPhase":8,"completed":true,"notes":{"acceptance":"Signed off"},"modelStates":{"waterfall":{"currentPhase":2,"completed":false,"notes":{"implementation":"Preserved"}}}}""", true, "V Model with inactive Waterfall");
        Check("""{"model":"agile","currentPhase":6,"completed":true,"notes":{"review":"Learned"},"cycle":2,"history":[{"cycle":1,"notes":{"backlog":"First sprint"}}]}""", true, "Agile cycle history");
        Check("""{"model":"iterative","currentPhase":5,"completed":true,"notes":{"evaluate":"Feedback"}}""", true, "Iterative completion");
        Check("""{"model":"iterative","currentPhase":5,"completed":true,"notes":{"evaluate":"Approved"},"release":{"currentPhase":0,"completed":false,"notes":{"deployment":"Release plan"}}}""", true, "Iterative deployment after evaluation");
        Check("""{"model":"iterative","currentPhase":5,"completed":true,"notes":{},"release":{"currentPhase":1,"completed":true,"notes":{"deployment":"Shipped","maintenance":"Monitored"}}}""", true, "Iterative completed release");
        Check("""{"model":"iterative","currentPhase":0,"completed":false,"notes":{},"cycle":2,"history":[{"cycle":1,"notes":{"evaluate":"Approved"},"release":{"currentPhase":1,"completed":true,"notes":{"deployment":"Shipped"}}}]}""", true, "Archived release alongside iteration notes");
        Check("""{"model":"waterfall","currentPhase":0,"completed":false,"notes":{},"modelStates":{"iterative":{"currentPhase":5,"completed":true,"notes":{},"release":{"currentPhase":1,"completed":false,"notes":{"maintenance":"Monitoring"}}}}}""", true, "Inactive iterative release progress");
        Check("""{"model":"iterative","currentPhase":5,"completed":false,"notes":{},"release":{"currentPhase":0,"completed":false,"notes":{}}}""", false, "release before evaluation complete");
        Check("""{"model":"iterative","currentPhase":5,"completed":true,"notes":{},"release":{"currentPhase":0,"completed":true,"notes":{}}}""", false, "premature release completion");
        Check("""{"model":"iterative","currentPhase":5,"completed":true,"notes":{},"release":{"currentPhase":2,"completed":false,"notes":{}}}""", false, "invalid release milestone");
        Check("""{"model":"iterative","currentPhase":5,"completed":true,"notes":{},"release":{"currentPhase":1,"completed":false,"notes":{"build":"Wrong path"}}}""", false, "invalid release notes");
        Check("""{"model":"iterative","currentPhase":5,"completed":true,"notes":{},"release":null}""", false, "malformed release");
        Check("""{"model":"agile","currentPhase":6,"completed":true,"notes":{},"release":{"currentPhase":0,"completed":false,"notes":{}}}""", false, "release path on wrong model");
        Check("""{"model":"iterative","currentPhase":0,"completed":false,"notes":{},"cycle":2,"history":[{"cycle":1,"notes":{},"release":{"currentPhase":1,"completed":false,"notes":{}}}]}""", false, "incomplete archived release");
        Check("""{"model":"v-model","currentPhase":9,"completed":false,"notes":{}}""", false, "phase outside model");
        Check("""{"model":"agile","currentPhase":3,"completed":true,"notes":{}}""", false, "premature cycle completion");
        Check("""{"model":"agile","currentPhase":0,"completed":false,"notes":{"maintenance":"Wrong phase"}}""", false, "model-specific notes");
        Check("""{"model":"agile","currentPhase":0,"completed":false,"notes":{},"cycle":2}""", false, "missing cycle history");
        Check("""{"model":"agile","currentPhase":0,"completed":false,"notes":{},"cycle":2,"history":[{"cycle":2,"notes":{}}]}""", false, "out-of-order history");
        Check("""{"model":"waterfall","currentPhase":0,"completed":false,"notes":{},"cycle":2}""", false, "sequential model cycle");
        Check("""{"model":"agile","currentPhase":0,"completed":false,"notes":{},"modelStates":{"waterfall":{"currentPhase":6,"completed":false,"notes":{}}}}""", false, "invalid inactive model");
        Check("""{"model":"agile","currentPhase":0,"completed":false,"notes":{},"modelStates":{"agile":{"currentPhase":0,"completed":false,"notes":{}}}}""", false, "duplicate active model");
        Check("""{"model":"iterative","currentPhase":{},"completed":false,"notes":{}}""", false, "malformed numeric property");
        Console.WriteLine("SDLC model, legacy compatibility, cycle history and iterative release checks passed.");
    }
}
