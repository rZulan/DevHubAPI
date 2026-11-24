using System.Text.Json;
using DevHub.Application.Connections;
using DevHub.Application.ProjectTools;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Realtime;

internal sealed class ConnectionChangeNotifier(IProjectToolsService documents, WorkshopRealtime realtime,
    IHubContext<WorkshopHub> hub) : IConnectionChangeNotifier
{
    public async Task Changed(ConnectionScope scope, ConnectionNodeRef target, CancellationToken ct)
    {
        var tool = target.Kind switch
        {
            "todo" or "todo-board" or "todo-column" or "todo-task" => "todo",
            "bug-reports" or "bug-report" or "bug-category" => "bug-reports",
            "erd" or "erd-table" or "erd-column" or "erd-relationship" => "erd",
            "calendar" or "calendar-entry" => "calendar",
            "ui-editor" or "ui-screen" or "ui-layer" => "ui-editor",
            "sdlc" or "sdlc-model" or "sdlc-cycle" or "sdlc-phase" or "sdlc-release" => "sdlc",
            "uml" or "uml-diagram" or "uml-element" or "uml-relationship" => "uml",
            "wiki" or "wiki-page" => "wiki",
            "notes" or "sticky-note" => "notes",
            _ => null
        };
        if (tool is null) return;

        var revision = 0;
        var notifyTodo = false;
        if (tool is not ("todo" or "bug-reports"))
        {
            var document = await documents.Get(scope.OrganizationId, scope.ProjectId, scope.UserId, tool, ct);
            if (!document.IsSuccess) return;
            revision = document.Value!.Revision;
            notifyTodo = tool == "sdlc" && document.Value.Document is { ValueKind: JsonValueKind.Object } roadmap &&
                roadmap.TryGetProperty("connectToTodo", out var enabled) && enabled.ValueKind == JsonValueKind.True;
        }

        await realtime.Revalidate(scope.OrganizationId, scope.ProjectId);
        var clients = hub.Clients.Group(WorkshopHub.GetProjectToolsGroupName(scope.OrganizationId, scope.ProjectId));
        await clients.SendAsync("ProjectToolChanged", scope.ProjectId.ToString(), tool, revision, ct);
        if (notifyTodo) await clients.SendAsync("ProjectToolChanged", scope.ProjectId.ToString(), "todo", 0, ct);
    }
}
