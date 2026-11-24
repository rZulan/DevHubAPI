using System.Text.Json;
using System.Text.Json.Nodes;

namespace DevHub.Infrastructure.ProjectTools;

internal static class SdlcCustomization
{
    public static bool TryPrepareSave(JsonElement prior, JsonElement draft, Guid editorId, out JsonElement document)
    {
        var normalized = JsonNode.Parse(draft.GetRawText())!.AsObject();
        // Setup options and older inactive progress are owned by the saved roadmap.
        foreach (var property in new[] { "roadmapId", "connectToTodo", "modelStates" })
            if (prior.TryGetProperty(property, out var previous))
                normalized[property] = JsonNode.Parse(previous.GetRawText());
            else
                normalized.Remove(property);
        document = JsonSerializer.SerializeToElement(normalized);

        var creatorId = prior.TryGetProperty("creatorId", out var creator) && creator.ValueKind == JsonValueKind.String
            && Guid.TryParse(creator.GetString(), out var savedCreator) ? savedCreator : (Guid?)null;
        if (creatorId != editorId && DeliverablesChanged(prior, document))
        {
            if (creatorId is not null) return false;
            creatorId = editorId;
        }
        if (creatorId is { } id) normalized["creatorId"] = id.ToString();
        else normalized.Remove("creatorId");
        document = JsonSerializer.SerializeToElement(normalized);
        return true;
    }

    private static bool DeliverablesChanged(JsonElement prior, JsonElement draft)
    {
        var before = Deliverables(prior);
        var after = Deliverables(draft);
        return before.Count != after.Count || before.Any(item =>
            !after.TryGetValue(item.Key, out var value) || !JsonElement.DeepEquals(item.Value, value));
    }

    private static Dictionary<(string Model, int Cycle, bool Release, string Phase), JsonElement> Deliverables(JsonElement roadmap)
    {
        var lists = new Dictionary<(string, int, bool, string), JsonElement>();
        if (roadmap.ValueKind != JsonValueKind.Object) return lists;

        AddModel(roadmap.GetProperty("model").GetString()!, roadmap);
        if (roadmap.TryGetProperty("modelStates", out var models))
            foreach (var model in models.EnumerateObject()) AddModel(model.Name, model.Value);
        return lists;

        void AddModel(string model, JsonElement progress)
        {
            AddCycle(model, progress.TryGetProperty("cycle", out var number) ? number.GetInt32() : 1, progress);
            if (progress.TryGetProperty("history", out var history))
                foreach (var cycle in history.EnumerateArray()) AddCycle(model, cycle.GetProperty("cycle").GetInt32(), cycle);
        }

        void AddCycle(string model, int cycle, JsonElement progress)
        {
            AddWork(model, cycle, false, progress);
            if (progress.TryGetProperty("release", out var release)) AddWork(model, cycle, true, release);
        }

        void AddWork(string model, int cycle, bool release, JsonElement progress)
        {
            if (!progress.TryGetProperty("work", out var work)) return;
            foreach (var phase in work.EnumerateObject())
                if (phase.Value.TryGetProperty("deliverables", out var items)) lists[(model, cycle, release, phase.Name)] = items;
        }
    }
}
