using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevHub.Application.Connections;

namespace DevHub.Infrastructure.Connections;

internal static partial class SdlcConnections
{
    private static readonly string[] ProgressKeys = ["currentPhase", "completed", "notes", "work", "cycle", "history", "release", "released"];

    internal static bool Apply(JsonObject document, ConnectionAction action)
    {
        var model = document["model"]?.GetValue<string>() ?? "waterfall";
        var target = action.Target;
        string Argument(string key) => action.Arguments.GetValueOrDefault(key) ?? "";
        if (action.Kind is "activate-model" or "set-active-model")
            return Activate(document, model, action.Kind == "activate-model" ? target.Id ?? "" : Argument("model"));
        if (target.Kind != "sdlc" && (target.Kind == "sdlc-model" ? target.Id : target.ParentId) != model)
            throw new ArgumentException("Switch to the linked roadmap model before editing its progress.");

        var definition = Models[model];
        var cycle = document["cycle"]?.GetValue<int>() ?? 1;
        var cycleId = cycle.ToString(CultureInfo.InvariantCulture);
        var release = target.Kind == "sdlc-release" || target.Kind == "sdlc-phase" && target.Id?.StartsWith($"{cycleId}:release:", StringComparison.Ordinal) == true;
        if (target.Kind is "sdlc-cycle" or "sdlc-release" && target.Id != cycleId) throw new ArgumentException("The linked cycle is archived.");
        if (action.Kind == "start-cycle") return StartNextCycle(document, model, definition, cycle);
        if (action.Kind == "start-release")
        {
            if (model != "iterative" || document["completed"]?.GetValue<bool>() != true || document["release"] is not null)
                throw new ArgumentException("Finish the iteration before starting its release path.");
            document["release"] = EmptyProgress();
            return true;
        }

        if (!release && document["release"] is not null) throw new ArgumentException("Continue in the active release path.");
        var progress = release ? document["release"] as JsonObject ?? throw new ArgumentException("The linked release is unavailable.") : document;
        var phases = release ? ReleasePhases : definition.Phases;
        var current = progress["currentPhase"]!.GetValue<int>();
        var index = target.Kind == "sdlc-phase"
            ? Array.FindIndex(phases, phase => target.Id == $"{cycleId}:{(release ? "release:" : "")}{phase.Key}") : current;
        if (index < 0) throw new ArgumentException("This phase belongs to an earlier cycle.");
        var phase = phases[index];

        if (action.Kind == "advance") return AdvancePhase(progress, model, phases, index, current);
        if (action.Kind == "set-current")
        {
            if (current == index && progress["completed"]?.GetValue<bool>() != true) return false;
            if (index > current && phases[current..index].Any(item => progress["work"]?[item.Key]?["blocked"]?.GetValue<bool>() == true))
                throw new ArgumentException("Clear blockers before skipping phases.");
            if (model == "agile" && progress["released"] is null) progress["released"] = current >= 6;
            progress["currentPhase"] = index;
            progress["completed"] = false;
            return true;
        }
        if (action.Kind is "set-phase-note" or "append-phase-note")
        {
            var notes = progress["notes"]!.AsObject();
            var prior = notes[phase.Key]?.GetValue<string>() ?? "";
            var value = Argument("note");
            if (action.Kind == "append-phase-note" && prior.Length > 0) value = $"{prior}\n\n{value}";
            return value != prior && Set(notes, phase.Key, JsonValue.Create(value));
        }
        var workItems = progress["work"] as JsonObject ?? new JsonObject();
        var phaseWork = workItems[phase.Key] as JsonObject ?? new JsonObject();
        bool changed = action.Kind switch
        {
            "set-owner" => SetOptional(phaseWork, "ownerId", Argument("ownerId")),
            "set-date" => SetOptional(phaseWork, "targetDate", Argument("targetDate")),
            "block" => Set(phaseWork, "blocked", JsonValue.Create(true)) | SetOptional(phaseWork, "blocker", Argument("blocker")),
            "unblock" => phaseWork.Remove("blocked") | phaseWork.Remove("blocker"),
            "check-deliverable" or "uncheck-deliverable" => CheckDeliverable(phase, phaseWork, Argument("item"), action.Kind == "check-deliverable"),
            _ => throw new ArgumentException("Unknown roadmap action.")
        };
        if (changed && phaseWork.Parent is null) workItems[phase.Key] = phaseWork;
        if (changed && workItems.Parent is null) progress["work"] = workItems;
        return changed;
    }

    private static bool AdvancePhase(JsonObject progress, string model, Phase[] phases, int index, int current)
    {
        if (progress["completed"]?.GetValue<bool>() == true) return false;
        if (index != current) throw new ArgumentException("Only the current phase can be advanced.");
        var phase = phases[index];
        var work = JsonSerializer.SerializeToElement(progress["work"]?[phase.Key]);
        if (Flag(work, "blocked") || !Ready(phase, work))
            throw new ArgumentException("Clear the blocker and check the phase deliverables before advancing.");
        if (model == "agile") progress["released"] = progress["released"]?.GetValue<bool>() == true || current == 5;
        if (index == phases.Length - 1) progress["completed"] = true;
        else progress["currentPhase"] = model == "agile" && current == 4 ? 6 : index + 1;
        return true;
    }

    private static bool Activate(JsonObject root, string model, string desired)
    {
        if (!Models.ContainsKey(desired)) throw new ArgumentException("Select a supported roadmap model.");
        if (model == desired) return false;
        var states = root["modelStates"] as JsonObject ?? new JsonObject();
        var restored = states[desired] as JsonObject ?? EmptyProgress();
        var archived = new JsonObject();
        foreach (var key in ProgressKeys)
        {
            if (root[key] is { } value) archived[key] = value.DeepClone();
            if (restored[key] is { } saved) root[key] = saved.DeepClone();
            else root.Remove(key);
        }
        states[model] = archived;
        states.Remove(desired);
        if (states.Parent is null) root["modelStates"] = states;
        root["model"] = desired;
        return true;
    }

    private static bool StartNextCycle(JsonObject root, string model, Model definition, int cycle)
    {
        if (definition.CycleLabel is null || root["completed"]?.GetValue<bool>() != true ||
            root["release"] is JsonObject release && release["completed"]?.GetValue<bool>() != true || cycle >= 1001)
            throw new ArgumentException("Complete the current cycle and any active release before starting another cycle.");
        var history = root["history"] as JsonArray ?? new JsonArray();
        var archive = new JsonObject { ["cycle"] = cycle, ["notes"] = root["notes"]!.DeepClone() };
        foreach (var key in new[] { "work", "release" }) if (root[key] is { } value) archive[key] = value.DeepClone();
        if (model == "agile")
        {
            archive["released"] = root["released"]?.GetValue<bool>() ?? true;
            root["released"] = false;
        }
        history.Add(archive);
        if (history.Parent is null) root["history"] = history;
        root["cycle"] = cycle + 1;
        root["currentPhase"] = model == "agile" ? 1 : 0;
        root["completed"] = false;
        root["notes"] = new JsonObject();
        root.Remove("work");
        root.Remove("release");
        return true;
    }

    private static bool CheckDeliverable(Phase phase, JsonObject work, string item, bool check)
    {
        if (!Deliverables(phase).Contains(item, StringComparer.Ordinal)) throw new ArgumentException("Choose a deliverable listed for this phase.");
        var checkedItems = work["checked"] as JsonArray ?? new JsonArray();
        var existing = checkedItems.FirstOrDefault(value => value?.GetValue<string>() == item);
        if (check == (existing is not null)) return false;
        if (check) checkedItems.Add(item);
        else checkedItems.Remove(existing);
        if (checkedItems.Parent is null) work["checked"] = checkedItems;
        return true;
    }

    private static bool SetOptional(JsonObject item, string key, string value) => value.Length == 0 ? item.Remove(key) : Set(item, key, JsonValue.Create(value));
    private static bool Set(JsonObject item, string key, JsonNode? value)
    {
        if (JsonNode.DeepEquals(item[key], value)) return false;
        item[key] = value;
        return true;
    }

    private static JsonObject EmptyProgress() => new() { ["currentPhase"] = 0, ["completed"] = false, ["notes"] = new JsonObject() };
}
