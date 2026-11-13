using System.Text.Json;
using DevHub.Application.Common;
using DevHub.Application.Ideas;
using DevHub.Application.ProjectTools;
using DevHub.Domain.Projects;
using DevHub.Domain.Users;
using DevHub.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class ProjectDocumentPersistenceChecks
{
    // Runs only in the disposable database: separate sessions must see committed fixtures.
    public static async Task Run(IServiceProvider provider)
    {
        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = await db.Projects.AsNoTracking().FirstAsync();
        var owner = await db.Organizations.Where(org => org.Id == source.OrganizationId).Select(org => org.OwnerUserId).SingleAsync();
        var project = Project.Create(source.OrganizationId, source.TeamId, owner, "Document race checks", "Concurrent saves", ProjectStatus.Planning,
            [], null, null, null, null, null, null, [], null, null, DateTimeOffset.UtcNow);
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        var tools = scope.ServiceProvider.GetRequiredService<IProjectToolsService>();
        var ideas = scope.ServiceProvider.GetRequiredService<IIdeasService>();
        foreach (var tool in new[] { "erd", "ui-editor", "sdlc", "notes", "wiki", "calendar", "uml" })
        {
            var initial = Require(await tools.Get(project.OrganizationId, project.Id, owner, tool, default));
            Check(initial.Revision == 0 && initial.Document is null, $"New projects retain the empty {tool} response without a saved record");
        }
        var canvas = Require(await ideas.Get(project.OrganizationId, project.Id, owner, default));
        Check(canvas.Revision == 0 && canvas.Items.GetArrayLength() == 0, "New Ideas canvases retain the empty array response");
        var emptyErd = JsonSerializer.Deserialize<JsonElement>("{\"tables\":[],\"relationships\":[]}");
        Check((await tools.Save(project.OrganizationId, project.Id, owner, "erd", new(5, emptyErd), default)).Error?.Type == ErrorType.Conflict &&
            !await db.ProjectDocuments.AnyAsync(document => document.ProjectId == project.Id),
            "A missing document only accepts revision zero on its first save");

        foreach (var (tool, json) in new[] { ("erd", "{\"tables\":[],\"relationships\":[]}"), ("ideas", "[]"), ("calendar", "{\"timeZone\":\"UTC\",\"events\":[]}") })
        {
            var document = JsonSerializer.Deserialize<JsonElement>(json);
            foreach (var revision in new[] { 0, 1 })
            {
                var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var attempts = Enumerable.Range(0, 8).Select(async _ =>
                {
                    await using var writer = provider.CreateAsyncScope();
                    await ready.Task;
                    if (tool == "ideas")
                    {
                        var result = await writer.ServiceProvider.GetRequiredService<IIdeasService>()
                            .Save(project.OrganizationId, project.Id, owner, new(revision, document), default);
                        return (result.IsSuccess, result.Error?.Type);
                    }
                    else
                    {
                        var result = await writer.ServiceProvider.GetRequiredService<IProjectToolsService>()
                            .Save(project.OrganizationId, project.Id, owner, tool, new(revision, document), default);
                        return (result.IsSuccess, result.Error?.Type);
                    }
                }).ToArray();
                ready.SetResult();
                var results = await Task.WhenAll(attempts);
                Check(results.Count(result => result.IsSuccess) == 1 && results.Count(result => result.Type == ErrorType.Conflict) == 7,
                    $"Eight concurrent {tool} {(revision == 0 ? "first" : "subsequent")} saves produce one winner and seven conflicts");
            }
            var saved = await db.ProjectDocuments.AsNoTracking().SingleAsync(row => row.ProjectId == project.Id && row.ToolType == tool);
            Check(saved.ContentJson == json && saved.Revision == 2 && saved.UpdatedByUserId == owner && saved.UpdatedAtUtc is not null,
                $"{tool} persists content, independent revision, editor and UTC save time");
        }

        var erd = await db.ProjectDocuments.AsNoTracking().SingleAsync(document => document.ProjectId == project.Id && document.ToolType == "erd");
        await Rejected($"""
            INSERT INTO ProjectDocuments (Id, ProjectId, ToolType, ContentJson, Revision)
            VALUES (NEWID(), {project.Id}, N'erd', N'[]', 0)
            """, "SQL rejects duplicate project/tool documents", 2601, 2627);
        await Rejected($"UPDATE ProjectDocuments SET ContentJson = N'invalid JSON' WHERE Id = {erd.Id}", "SQL rejects malformed JSON", 547);
        await Rejected($"UPDATE ProjectDocuments SET Revision = -1 WHERE Id = {erd.Id}", "SQL rejects negative document revisions", 547);
        await Rejected($"UPDATE ProjectDocuments SET ToolType = N'unknown' WHERE Id = {erd.Id}", "SQL rejects unsupported tool types", 547);

        var editor = User.Create("retired-editor@example.test", "retired_editor", "Retired", "Editor", "test-only-hash", DateTimeOffset.UtcNow);
        db.Users.Add(editor);
        await db.SaveChangesAsync();
        await db.ProjectDocuments.Where(document => document.Id == erd.Id).ExecuteUpdateAsync(update => update.SetProperty(document => document.UpdatedByUserId, editor.Id));
        await db.Users.Where(user => user.Id == editor.Id).ExecuteDeleteAsync();
        var retained = await db.ProjectDocuments.AsNoTracking().SingleAsync(document => document.Id == erd.Id);
        Check(retained.UpdatedByUserId is null && retained.ContentJson == erd.ContentJson && retained.Revision == erd.Revision,
            "Deleting a former editor clears attribution and preserves their document");

        await db.Projects.Where(row => row.Id == project.Id).ExecuteDeleteAsync();
        Check(!await db.ProjectDocuments.AnyAsync(document => document.ProjectId == project.Id), "Deleting a project removes its documents through the foreign key");

        async Task Rejected(FormattableString sql, string label, params int[] errorNumbers)
        {
            try { await db.Database.ExecuteSqlAsync(sql); }
            catch (SqlException exception) when (errorNumbers.Contains(exception.Number)) { Check(true, label); return; }
            throw new Exception(label);
        }
    }

    private static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new Exception(result.Error!.Code);
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
}
