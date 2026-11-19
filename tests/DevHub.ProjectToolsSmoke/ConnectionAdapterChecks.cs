using System.Text.Json;
using DevHub.Application.Common;
using DevHub.Application.Connections;
using DevHub.Application.ProjectTools;
using Microsoft.Extensions.DependencyInjection;

internal static class ConnectionAdapterChecks
{
    public static async Task Run(IServiceProvider services, ConnectionScope scope, CancellationToken ct)
    {
        var adapters = services.GetRequiredService<IConnectionTools>();
        var documents = services.GetRequiredService<IProjectToolsService>();

        async Task<ConnectionNodeRef> Create(ConnectionNodeRef target, string kind, params (string Key, string Value)[] arguments) =>
            (await Apply(target, kind, arguments)).CreatedNode ?? throw new InvalidOperationException($"{kind} returned no node ID.");
        async Task<ConnectionActionResult> Apply(ConnectionNodeRef target, string kind, params (string Key, string Value)[] arguments) =>
            Require(await adapters.Execute(scope, new(Guid.NewGuid(), target, kind, arguments.ToDictionary(pair => pair.Key, pair => pair.Value)), ct));
        async Task<JsonElement> Load(string tool) => Require(await documents.Get(scope.OrganizationId, scope.ProjectId, scope.UserId, tool, ct)).Document ??
            throw new InvalidOperationException($"{tool} returned no document.");

        var note = await Create(new("notes"), "create-note", ("title", "Migration notes"), ("body", "First"));
        await Apply(note, "append-body", ("body", "Second"));
        Check(Require(await adapters.Check(scope, new(note, "body-contains", new Dictionary<string, string> { ["text"] = "second" }), ct)),
            "Note body conditions read committed Markdown case-insensitively");
        Check((await Load("notes")).GetProperty("notes")[0].GetProperty("body").GetString() == "First\n\nSecond", "Notes append preserves the earlier body");

        var wiki = await Create(new("wiki"), "create-page", ("title", "Migration docs"), ("body", "Original"));
        var child = await Create(wiki, "create-subpage", ("title", "Details"));
        await Apply(wiki, "set-title", ("title", "Deployment docs"));
        var page = (await Load("wiki")).GetProperty("pages")[0];
        Check(page.GetProperty("slug").GetString() == "migration-docs" && page.GetProperty("history")[0].GetProperty("title").GetString() == "Migration docs",
            "Wiki actions retain slug and revision history");
        await Apply(wiki, "delete");
        var promoted = await Load("wiki");
        Check(promoted.GetProperty("homePageId").GetString() == child.Id && promoted.GetProperty("pages")[0].GetProperty("parentId").ValueKind == JsonValueKind.Null,
            "Wiki deletion promotes children and selects home");

        var screen = await Create(new("ui-editor"), "create-screen", ("name", "Onboarding"));
        var button = await Create(screen, "create-layer", ("name", "Continue"), ("kind", "button"));
        await Apply(button, "configure-interaction", ("interaction", "navigate"), ("targetId", screen.Id!));
        var duplicate = await Create(screen, "duplicate-screen");
        await Apply(screen, "delete");
        var design = await Load("ui-editor");
        Check(!design.TryGetProperty("startFrameId", out _) && design.GetProperty("layers").GetArrayLength() == 2, "Deleting the start screen cleans its original children");
        var copiedButton = design.GetProperty("layers").EnumerateArray().Single(layer => layer.GetProperty("kind").GetString() == "button");
        Check(copiedButton.GetProperty("parentId").GetString() == duplicate.Id && copiedButton.GetProperty("interaction").GetProperty("targetId").GetString() == duplicate.Id,
            "Screen duplication remaps child and prototype references");

        var diagram = await Create(new("uml"), "create-diagram", ("name", "Project structure"), ("type", "class"));
        var user = await Create(diagram, "add-element", ("symbol", "class"), ("text", "User"));
        var project = await Create(diagram, "add-element", ("symbol", "class"), ("text", "Project"));
        var association = await Create(diagram, "add-uml-relationship", ("symbol", "association"), ("startId", user.Id!), ("endId", project.Id!));
        await Apply(association, "reverse-relation");
        var connected = (await Load("uml")).GetProperty("diagrams")[0].GetProperty("items")[2].GetProperty("connection");
        Check(connected.GetProperty("start").GetProperty("shapeId").GetString() == project.Id, "UML relationship reversal swaps its endpoints");
        await Apply(user, "delete");
        Check((await Load("uml")).GetProperty("diagrams")[0].GetProperty("items").GetArrayLength() == 1, "UML element deletion cleans attached relationships");
    }

    private static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new InvalidOperationException(result.Error!.Description);

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
