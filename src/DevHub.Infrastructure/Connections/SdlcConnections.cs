using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using DevHub.Application.Connections;
using static DevHub.Infrastructure.Connections.ConnectionCatalog;

namespace DevHub.Infrastructure.Connections;

internal static partial class SdlcConnections
{
    private sealed record Phase(string Key, string Title, string Deliverables, bool Ongoing = false, bool Optional = false);
    private sealed record Model(string Title, Phase[] Phases, string? CycleLabel = null);

    private static readonly Phase[] Waterfall =
    [new("requirements", "Requirements", "Requirements specification, stakeholder approval"),
        new("design", "System design", "Architecture, ERD, UI designs"),
        new("implementation", "Implementation", "Working code, unit tests, technical documentation"),
        new("verification", "Verification", "Test results, defect fixes, acceptance sign-off"),
        new("deployment", "Deployment", "Release plan, production deployment, user training"),
        new("maintenance", "Maintenance", "Monitoring, support log, maintenance releases")];
    private static readonly Phase[] ReleasePhases = [Waterfall[4], Waterfall[5]];

    private static readonly IReadOnlyDictionary<string, Model> Models = new Dictionary<string, Model>
    {
        ["waterfall"] = new("Waterfall", Waterfall),
        ["v-model"] = new("V Model",
        [new("requirements", "Requirements", "User requirements, acceptance test plan"),
            new("system-design", "System design", "System specification, system test plan"),
            new("architecture", "Architecture", "Architecture, interface contracts, integration test plan"),
            new("module-design", "Module design", "Module specifications, unit test cases"),
            new("implementation", "Implementation", "Working code, build artifacts, code review"),
            new("unit", "Unit testing", "Unit test results, module defect fixes"),
            new("integration", "Integration testing", "Integration test results, interface defect fixes"),
            new("system-testing", "System testing", "System test results, performance checks"),
            new("acceptance", "Acceptance testing", "Acceptance results, stakeholder sign-off")]),
        ["agile"] = new("Agile",
        [new("backlog", "Backlog refinement", "Ordered Product Backlog, clarified acceptance criteria", Ongoing: true),
            new("planning", "Sprint planning", "Sprint Goal, Sprint Backlog, initial delivery plan"),
            new("design", "Design", "Design sketches, technical approach"),
            new("build", "Build", "Integrated increment, reviewed code"),
            new("test", "Done increment", "Verified increment, Definition of Done evidence"),
            new("release", "Release", "Released increment, release notes", Optional: true),
            new("review", "Sprint Review", "Stakeholder feedback, adapted Product Backlog"),
            new("retrospective", "Retrospective", "Team improvements, next Sprint adaptations")], "Sprint"),
        ["iterative"] = new("Iterative",
        [new("planning", "Plan", "Iteration goals, prioritized improvements"),
            new("analysis", "Analyze", "Explored requirements, risk assessment"),
            new("design", "Design", "Updated designs, implementation plan"),
            new("build", "Build", "Updated software, technical documentation"),
            new("test", "Test", "Test results, regression results, defect fixes"),
            new("evaluate", "Evaluate", "Evaluation findings, stakeholder feedback, release readiness decision")], "Iteration"),
    };

    private static readonly ConnectionNodeRef Root = new("sdlc");
    private static readonly ConnectionField ModelField = new("model", "Roadmap model", "select", true,
        Models.Select(pair => new ConnectionOption(pair.Key, pair.Value.Title)).ToArray());
    private static readonly ConnectionField StatusField = Select("status", "Phase status", ["current", "completed", "blocked", "upcoming", "optional", "ongoing"]);
    private static readonly ConnectionChoice Exists = Choice("exists", "Node exists");
    private static readonly ConnectionChoice Complete = Choice("completed", "Work is complete");
    private static readonly ConnectionChoice Advance = Choice("advance", "Complete the current phase");
    private static readonly ConnectionChoice StartCycle = Choice("start-cycle", "Start next cycle");
    private static readonly ConnectionChoice StartRelease = Choice("start-release", "Start release");
    private static readonly ConnectionChoice[] PhaseEvents =
    [Choice("phase-current", "Phase became current"), Choice("phase-completed", "Phase completed"),
        Choice("phase-reopened", "Phase reopened"), Choice("phase-blocked", "Phase blocked"), Choice("phase-unblocked", "Phase unblocked"),
        Choice("phase-status-changed", "Phase status changed", StatusField with { Required = false }), Choice("phase-updated", "Phase work or notes updated")];
    private static readonly ConnectionChoice[] CycleEvents =
    [Choice("cycle-started", "Cycle started"), Choice("cycle-completed", "Cycle completed"),
        Choice("release-started", "Release started"), Choice("release-completed", "Release completed")];

    internal static IReadOnlyList<ConnectionCapability> Capabilities { get; } =
    [new("sdlc", "sdlc", "SDLC Roadmap", [Choice("active-model-changed", "Active model changed", ModelField with { Required = false }), .. CycleEvents, .. PhaseEvents],
            [Exists, Choice("active-model-is", "Active model is", ModelField)], [Choice("set-active-model", "Switch active model", ModelField), Advance, StartCycle, StartRelease]),
        new("sdlc-model", "sdlc", "Roadmap model", [Choice("active-model-changed", "This model became active"), Choice("model-updated", "Model progress updated"), .. CycleEvents, .. PhaseEvents],
            [Exists, Choice("model-active", "This model is active"), Complete], [Choice("activate-model", "Make this model active"), Advance, StartCycle, StartRelease]),
        new("sdlc-cycle", "sdlc", "Sprint or iteration", [Choice("cycle-completed", "Cycle completed"), Choice("cycle-updated", "Cycle progress updated"), .. PhaseEvents],
            [Exists, Complete], [Advance, StartCycle, StartRelease]),
        new("sdlc-release", "sdlc", "Release", [Choice("release-completed", "Release completed"), Choice("release-updated", "Release progress updated"), .. PhaseEvents],
            [Exists, Complete], [Advance, StartCycle]),
        new("sdlc-phase", "sdlc", "Roadmap phase", PhaseEvents,
            [Exists, Choice("phase-status-is", "Phase status is", StatusField), Choice("phase-blocked", "Phase is blocked"),
                Choice("phase-owner-is", "Phase owner is", Field("ownerId", "Owner ID")), Choice("phase-checklist-done", "All phase deliverables are checked")],
            [Choice("set-current", "Set as current phase"), Advance, Choice("set-owner", "Set or clear phase owner", Field("ownerId", "Owner ID", required: false)),
                Choice("set-date", "Set or clear phase target date", Field("targetDate", "Target date", "date", false)),
                Choice("block", "Block phase", Field("blocker", "Blocker", "multiline", false)), Choice("unblock", "Clear phase blocker"),
                Choice("set-phase-note", "Replace phase notes", Field("note", "Notes", "multiline", false)),
                Choice("append-phase-note", "Append to phase notes", Field("note", "Notes", "multiline")),
                Choice("check-deliverable", "Check deliverable", Field("item", "Deliverable")), Choice("uncheck-deliverable", "Uncheck deliverable", Field("item", "Deliverable"))])];

    internal static IEnumerable<ConnectionSnapshot> Snapshots(JsonElement document)
    {
        var activeModel = Text(document, "model");
        foreach (var (model, definition) in Models)
        {
            var active = model == activeModel;
            var progress = active ? document : Property(Property(document, "modelStates"), model);
            var modelRef = new ConnectionNodeRef("sdlc-model", model);
            var path = $"SDLC Roadmap › {definition.Title}";
            var modelFields = new Dictionary<string, string> { ["name"] = definition.Title, ["model"] = model,
                ["active"] = active ? "true" : "false", ["completed"] = Flag(progress, "completed") ? "true" : "false" };
            yield return new(modelRef, definition.Title, path, [Root], modelFields, Track(progress, "currentPhase", "completed", "cycle", "released"));
            if (progress.ValueKind != JsonValueKind.Object) continue;

            var cycle = Number(progress, "cycle", 1).ToString(CultureInfo.InvariantCulture);
            ConnectionNodeRef[] ancestors = [modelRef, Root];
            if (definition.CycleLabel is { } cycleLabel)
            {
                var cycleRef = new ConnectionNodeRef("sdlc-cycle", cycle, model);
                var label = $"{cycleLabel} {cycle}";
                path += $" › {label}";
                yield return new(cycleRef, label, path, ancestors, new Dictionary<string, string>(modelFields)
                    { ["name"] = label, ["cycle"] = cycle }, Track(progress, "currentPhase", "completed", "released"));
                ancestors = [cycleRef, modelRef, Root];
            }
            foreach (var phase in PhaseSnapshots(progress, model, cycle, definition.Phases, path, ancestors, release: false)) yield return phase;

            var release = Property(progress, "release");
            if (release.ValueKind != JsonValueKind.Object) continue;
            var releaseRef = new ConnectionNodeRef("sdlc-release", cycle, model);
            const string releaseLabel = "Release path";
            var releasePath = $"{path} › {releaseLabel}";
            yield return new(releaseRef, releaseLabel, releasePath, ancestors, new Dictionary<string, string>(modelFields)
                { ["name"] = releaseLabel, ["cycle"] = cycle, ["completed"] = Flag(release, "completed") ? "true" : "false" }, Track(release, "currentPhase", "completed"));
            foreach (var phase in PhaseSnapshots(release, model, cycle, ReleasePhases, releasePath, [releaseRef, .. ancestors], release: true)) yield return phase;
        }
    }

    private static IEnumerable<ConnectionSnapshot> PhaseSnapshots(JsonElement progress, string model, string cycle, Phase[] phases,
        string path, IReadOnlyList<ConnectionNodeRef> ancestors, bool release)
    {
        var current = Number(progress, "currentPhase");
        var completed = Flag(progress, "completed");
        for (var index = 0; index < phases.Length; index++)
        {
            var phase = phases[index];
            var work = Property(Property(progress, "work"), phase.Key);
            var note = Property(Property(progress, "notes"), phase.Key);
            var done = !phase.Ongoing && (model == "agile" && phase.Optional
                ? current != 5 && (Property(progress, "released").ValueKind == JsonValueKind.Undefined ? current >= 6 : Flag(progress, "released"))
                : completed || index < current);
            var status = done ? "completed" : Flag(work, "blocked") ? "blocked" : phase.Ongoing ? "ongoing" : index == current ? "current" : phase.Optional ? "optional" : "upcoming";
            var fields = new Dictionary<string, string> { ["name"] = phase.Title, ["model"] = model, ["cycle"] = cycle, ["phase"] = phase.Key,
                ["status"] = status, ["blocked"] = Flag(work, "blocked") ? "true" : "false", ["current"] = !completed && index == current ? "true" : "false",
                ["ownerId"] = Text(work, "ownerId"), ["targetDate"] = Text(work, "targetDate"), ["checklistDone"] = Ready(phase, work) ? "true" : "false" };
            var tracked = Track(work, "ownerId", "targetDate", "blocked", "blocker", "checked");
            if (note.ValueKind == JsonValueKind.String) tracked["notes"] = note;
            yield return new(new("sdlc-phase", $"{cycle}:{(release ? "release:" : "")}{phase.Key}", model), phase.Title,
                $"{path} › {phase.Title}", ancestors, fields, tracked);
        }
    }

    internal static IEnumerable<ConnectionEventDraft> Events(JsonElement previous, JsonElement next)
    {
        var active = Text(next, "model");
        if (active.Length > 0 && active != Text(previous, "model"))
            yield return new(new("sdlc-model", active), "active-model-changed", new Dictionary<string, string> { ["model"] = active }, [Root]);

        var before = Snapshots(previous).ToDictionary(item => item.Reference);
        foreach (var item in Snapshots(next))
        {
            var kind = item.Reference.Kind;
            before.TryGetValue(item.Reference, out var old);
            if (kind is "sdlc-cycle" or "sdlc-release")
            {
                var prefix = kind == "sdlc-cycle" ? "cycle" : "release";
                if (old is null) yield return Event(item, $"{prefix}-started");
                else if (old.Fields["completed"] != "true" && item.Fields["completed"] == "true") yield return Event(item, $"{prefix}-completed");
            }
            if (kind != "sdlc-phase" || old is null) continue;
            var status = item.Fields["status"];
            var oldStatus = old.Fields["status"];
            if (status != oldStatus)
            {
                yield return Event(item, "phase-status-changed");
                if (status == "completed") yield return Event(item, "phase-completed");
                else if (oldStatus == "completed") yield return Event(item, "phase-reopened");
            }
            if (item.Fields["current"] == "true" && old.Fields["current"] != "true") yield return Event(item, "phase-current");
            if (item.Fields["blocked"] != old.Fields["blocked"])
                yield return Event(item, item.Fields["blocked"] == "true" ? "phase-blocked" : "phase-unblocked");
        }
    }

    internal static bool? Check(IReadOnlyList<ConnectionSnapshot> nodes, ConnectionCondition condition)
    {
        var fields = nodes.FirstOrDefault(item => item.Reference == condition.Node)?.Fields;
        return condition.Kind switch
        {
            "active-model-is" => nodes.Any(item => item.Reference.Kind == "sdlc-model" && item.Reference.Id == condition.Arguments.GetValueOrDefault("model") && item.Fields["active"] == "true"),
            "model-active" => fields?.GetValueOrDefault("active") == "true",
            "completed" => fields?.GetValueOrDefault("completed") == "true",
            "phase-status-is" => fields?.GetValueOrDefault("status") == condition.Arguments.GetValueOrDefault("status"),
            "phase-blocked" => fields?.GetValueOrDefault("blocked") == "true",
            "phase-owner-is" => fields?.GetValueOrDefault("ownerId") == condition.Arguments.GetValueOrDefault("ownerId"),
            "phase-checklist-done" => fields?.GetValueOrDefault("checklistDone") == "true",
            _ => null
        };
    }

    private static string[] Deliverables(Phase phase) => phase.Deliverables.Split(", ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static bool Ready(Phase phase, JsonElement work) => phase.Ongoing || Deliverables(phase).All(item =>
        ConnectionDocuments.Items(work, "checked").Any(value => value.GetString() == item));
    private static ConnectionEventDraft Event(ConnectionSnapshot node, string kind) => new(node.Reference, kind, node.Fields, node.Ancestors);
    private static JsonElement Property(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value) ? value : default;
    private static bool Flag(JsonElement node, string key) => Property(node, key).ValueKind == JsonValueKind.True;
    private static string Text(JsonElement node, string key) => ConnectionDocuments.Text(node, key) ?? "";
    private static int Number(JsonElement node, string key, int fallback = 0) => Property(node, key) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : fallback;
    private static Dictionary<string, JsonElement> Track(JsonElement node, params string[] keys) => keys.Select(key => (Key: key, Value: Property(node, key)))
        .Where(entry => entry.Value.ValueKind != JsonValueKind.Undefined).ToDictionary(entry => entry.Key, entry => entry.Value);
}
