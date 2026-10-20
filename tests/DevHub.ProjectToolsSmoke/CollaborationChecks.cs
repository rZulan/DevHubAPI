using System.Reflection;
using System.Security.Claims;
using DevHub.Api.Controllers;
using DevHub.Api.Realtime;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Ideas;
using DevHub.Application.ProjectTools;
using DevHub.Application.Todos;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json;

internal static class CollaborationChecks
{
    public static async Task Run()
    {
        var org = Guid.NewGuid(); var project = Guid.NewGuid(); var user = Guid.NewGuid();
        var registry = new WorkshopSubscriptions(); var allowed = true;
        var joined = new HashSet<(string Connection, string Group)>();
        var events = new List<(string Group, string Name, object?[] Args)>();
        var groups = ToolsStub.Create<IGroupManager>((method, args) =>
        {
            var pair = ((string)args![0]!, (string)args[1]!);
            if (method.Name == "AddToGroupAsync") joined.Add(pair); else if (method.Name == "RemoveFromGroupAsync") joined.Remove(pair); else throw new Exception(method.Name);
            return Task.CompletedTask;
        });
        var clients = ToolsStub.Create<IHubClients>((method, args) =>
        {
            if (method.Name != "Group") throw new Exception("Tool updates must target the project group only.");
            var group = (string)args![0]!;
            return ToolsStub.Create<IClientProxy>((_, values) => {events.Add((group, (string)values![0]!, (object?[])values[1]!)); return Task.CompletedTask;});
        });
        var context = ToolsStub.Create<IHubContext<WorkshopHub>>((method, _) => method.Name switch {"get_Groups" => groups, "get_Clients" => clients, _ => throw new Exception(method.Name)});
        var organizations = ToolsStub.Create<IOrganizationRepository>((_, _) => throw new Exception("Unexpected organization query"));
        var ideas = ToolsStub.Create<IIdeasService>((method, _) => method.Name == "CanView" ? Task.FromResult(allowed) : throw new Exception(method.Name));
        var presence = new WorkshopPresenceTracker(TimeProvider.System);
        var realtime = new WorkshopRealtime(registry, organizations, ideas, presence, context);
        var connection = new ToolsConnection("one", user);
        var hub = new WorkshopHub(organizations, ToolsStub.Create<IUserRepository>((_, _) => throw new Exception("Unexpected user query")), ideas, presence, new IdeasSelectionTracker(), registry, realtime) {Context = connection, Groups = groups};
        await hub.JoinProjectTools(org, project);
        var groupName = WorkshopHub.GetProjectToolsGroupName(org, project);
        Check(joined.Contains(("one", groupName)) && registry.ForOrganization(org).Single().Group == groupName, "Live tool subscription is scoped to the project");
        await hub.JoinProjectTools(org, project);
        Check(registry.ForOrganization(org).Length == 1, "Rejoining does not leak duplicate subscriptions");
        Check(groupName != WorkshopHub.GetIdeasGroupName(org, project) && groupName != WorkshopHub.GetProjectToolsGroupName(Guid.NewGuid(), project), "Tools and organizations have distinct live groups");

        var success = true;
        var tools = ToolsStub.Create<IProjectToolsService>((method, _) => method.Name == "Save"
            ? Task.FromResult(success ? Result<ToolDocumentResponse>.Success(new(7, JsonSerializer.Deserialize<JsonElement>("{\"tables\":[],\"relationships\":[]}"))) : Result<ToolDocumentResponse>.Failure(new("test", "denied", ErrorType.Forbidden))) : throw new Exception(method.Name));
        var controller = new ProjectToolsController(tools, context, realtime) {ControllerContext = Auth(user)};
        foreach (var tool in new[] {"erd", "ui-editor", "sdlc", "notes"})
        {
            events.Clear(); success = true;
            await controller.Save(org, project, tool, new(6, JsonSerializer.Deserialize<JsonElement>("{}")), default);
            var item = events.Single();
            Check(item.Group == groupName && item.Name == "ProjectToolChanged" && (string)item.Args[0]! == project.ToString() && (string)item.Args[1]! == tool && (int)item.Args[2]! == 7, $"{tool} saves broadcast its revision to the project only");
            events.Clear(); success = false;
            await controller.Save(org, project, tool, new(6, JsonSerializer.Deserialize<JsonElement>("{}")), default);
            Check(events.Count == 0, $"Failed {tool} saves do not broadcast");
        }
        var todos = ToolsStub.Create<ITodoService>((method, _) => method.Name switch {
            "Create" => Task.FromResult(success ? Result<TodoResponse>.Success(new(Guid.NewGuid(), project, "Test", "", null, DateTimeOffset.UtcNow, [], [], 0)) : Result<TodoResponse>.Failure(new("test", "denied", ErrorType.Forbidden))),
            "UpdateTask" => Task.FromResult(success ? Result<TodoTaskResponse>.Success(new(Guid.NewGuid(), Guid.NewGuid(), project, "Edited", "", "pending", null, [])) : Result<TodoTaskResponse>.Failure(new("test", "denied", ErrorType.Forbidden))),
            "DeleteTask" => Task.FromResult(success ? Result<bool>.Success(true) : Result<bool>.Failure(new("test", "denied", ErrorType.Forbidden))),
            _ => throw new Exception(method.Name)
        });
        var todoController = new TodosController(todos, context, realtime) {ControllerContext = Auth(user)};
        success = true; events.Clear(); await todoController.Create(org, project, new("Test", null, null), default);
        Check(events.Single().Group == groupName && (string)events.Single().Args[1]! == "todo", "TODO mutations notify other project sessions");
        success = false; events.Clear(); await todoController.Create(org, project, new("Test", null, null), default);
        Check(events.Count == 0, "Failed TODO mutations do not broadcast");
        foreach (var change in new[] {"edit", "delete"})
        {
            foreach (var ok in new[] {true, false})
            {
                success = ok; events.Clear();
                if (change == "edit") await todoController.UpdateTask(org, project, Guid.NewGuid(), Guid.NewGuid(), new("Edited", "", "pending", [], 0), default);
                else await todoController.DeleteTask(org, project, Guid.NewGuid(), Guid.NewGuid(), new(0), default);
                Check(ok ? events.Single().Group == groupName && (string)events.Single().Args[1]! == "todo" : events.Count == 0, $"TODO {change} broadcasts only on success");
            }
        }
        allowed = false; await realtime.Revalidate(org, project);
        Check(connection.Aborted && joined.Count == 0 && registry.ForOrganization(org).Length == 0, "Project access revocation removes live tool subscriptions");
        try {await hub.JoinProjectTools(org, project); throw new Exception("Unauthorized subscription accepted");} catch (HubException) {Check(joined.Count == 0, "Unauthorized users cannot subscribe to shared tool updates");}
    }
    private static ControllerContext Auth(Guid user) => new() {HttpContext = new DefaultHttpContext {User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", user.ToString())], "test"))}};
    private static void Check(bool condition, string label) {if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label);}
}
internal sealed class ToolsConnection(string id, Guid user) : HubCallerContext
{
    public bool Aborted {get; private set;}
    public override string ConnectionId => id;
    public override string? UserIdentifier => user.ToString();
    public override ClaimsPrincipal User {get;} = new(new ClaimsIdentity([new Claim("sub", user.ToString())], "test"));
    public override IDictionary<object, object?> Items {get;} = new Dictionary<object, object?>();
    public override IFeatureCollection Features {get;} = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() => Aborted = true;
}
public class ToolsStub : DispatchProxy
{
    public Func<MethodInfo, object?[]?, object?> Handler {get; set;} = null!;
    protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    public static T Create<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = Create<T, ToolsStub>(); ((ToolsStub)(object)proxy).Handler = handler; return proxy;
    }
}
