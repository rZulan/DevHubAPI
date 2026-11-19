using System.Text.Json;
using DevHub.Application.Common;
using DevHub.Application.Connections;
using DevHub.Application.ProjectTools;
using Microsoft.Extensions.DependencyInjection;

internal static class SdlcConnectionChecks
{
    public static async Task Run(IServiceProvider provider, ConnectionScope scope, CancellationToken ct)
    {
        var tools = provider.GetRequiredService<IConnectionTools>();
        var documents = provider.GetRequiredService<IProjectToolsService>();
        var roadmap = new ConnectionNodeRef("sdlc");
        var requirements = new ConnectionNodeRef("sdlc-phase", "1:requirements", "waterfall");
        var draft = JsonSerializer.Deserialize<JsonElement>("""{"model":"waterfall","currentPhase":0,"completed":false,"notes":{}}""");
        Require(await documents.Save(scope.OrganizationId, scope.ProjectId, scope.UserId, "sdlc", new(0, draft), ct));

        if (await tools.ResolveNode(scope, requirements, ct) is not { Label: "Requirements" })
            throw new InvalidOperationException("The SDLC phase picker did not resolve the saved Waterfall phase.");
        if ((await Apply("advance", requirements)).IsSuccess)
            throw new InvalidOperationException("A roadmap action advanced before the required checklist was complete.");
        Require(await Apply("check-deliverable", requirements, "item", "Requirements specification"));
        Require(await Apply("check-deliverable", requirements, "item", "stakeholder approval"));
        Require(await Apply("block", requirements, "blocker", "Review pending"));
        if ((await Apply("advance", requirements)).IsSuccess)
            throw new InvalidOperationException("A roadmap action advanced despite a blocker.");
        Require(await Apply("unblock", requirements));
        Require(await Apply("advance", requirements));
        Require(await Apply("set-phase-note", requirements, "note", "Reviewed in automation smoke test"));
        Require(await Apply("set-active-model", roadmap, "model", "agile"));
        Require(await Apply("set-active-model", roadmap, "model", "waterfall"));

        var response = Require(await documents.Get(scope.OrganizationId, scope.ProjectId, scope.UserId, "sdlc", ct));
        var saved = response.Document!.Value;
        if (saved.GetProperty("currentPhase").GetInt32() != 1 || saved.GetProperty("notes").GetProperty("requirements").GetString() != "Reviewed in automation smoke test")
            throw new InvalidOperationException("Automation did not preserve phase progress and notes across model switches.");
        Console.WriteLine("Connections SDLC node, native checklist, blocker and model history checks passed.");

        Task<Result<ConnectionActionResult>> Apply(string action, ConnectionNodeRef target, string? key = null, string? value = null) =>
            tools.Execute(scope, new(Guid.NewGuid(), target, action, key is null ? new Dictionary<string, string>() : new Dictionary<string, string> { [key] = value! }), ct);
    }

    private static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new InvalidOperationException(result.Error?.Description);
}
