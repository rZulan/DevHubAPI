using System.Text.Json;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

internal static class ProjectDocumentMigrationChecks
{
    public const string LegacyMigration = "20261026081801_AddProjectBugReports";

    public static async Task Run(ApplicationDbContext db, Guid projectId)
    {
        var marker = "Unicode ✓ 日本語 'quoted' " + new string('x', 6000);
        var erd = JsonSerializer.Serialize(new { tables = Array.Empty<object>(), relationships = Array.Empty<object>(), marker });
        const string roadmap = "{ \"model\": \"waterfall\", \"currentPhase\": 0, \"completed\": false, \"notes\": {} }";
        const string notes = "{\"notes\":[]}";
        await db.Database.ExecuteSqlAsync($"""
            UPDATE [Projects] SET [IdeasJson] = N'[]', [IdeasRevision] = 3,
                [ErdJson] = {erd}, [ErdRevision] = 7,
                [DesignJson] = NULL, [DesignRevision] = 4,
                [RoadmapJson] = {roadmap}, [RoadmapRevision] = 2,
                [NotesJson] = {notes}, [NotesRevision] = 0,
                [WikiJson] = NULL, [WikiRevision] = 0
            WHERE [Id] = {projectId}
            """);
        var expected = new Dictionary<string, (string? Json, int Revision)>
        {
            ["ideas"] = ("[]", 3), ["erd"] = (erd, 7), ["ui-editor"] = (null, 4),
            ["sdlc"] = (roadmap, 2), ["notes"] = (notes, 0), ["wiki"] = (null, 0)
        };
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync();
        await Verify("Migration");

        // A rollback must retain saves made after the migration, not just the original copy.
        const string latest = "{\"notes\":[{\"id\":\"latest\",\"body\":\"Saved after migration\"}]}";
        await db.ProjectDocuments.Where(document => document.ProjectId == projectId && document.ToolType == "notes")
            .ExecuteUpdateAsync(update => update.SetProperty(document => document.ContentJson, latest).SetProperty(document => document.Revision, 11));
        expected["notes"] = (latest, 11);
        await migrator.MigrateAsync(LegacyMigration);
        await using (var command = db.Database.GetDbConnection().CreateCommand())
        {
            await db.Database.OpenConnectionAsync();
            command.CommandText = "SELECT NotesJson, NotesRevision FROM Projects WHERE Id = @id";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@id"; parameter.Value = projectId;
            command.Parameters.Add(parameter);
            await using var reader = await command.ExecuteReaderAsync();
            Check(await reader.ReadAsync() && reader.GetString(0) == latest && reader.GetInt32(1) == 11,
                "Rollback restores the latest saved document and revision to Projects");
        }
        await db.Database.CloseConnectionAsync();
        await migrator.MigrateAsync();
        await Verify("Rollback and reapply");

        async Task Verify(string label)
        {
            var saved = await db.ProjectDocuments.AsNoTracking().Where(document => document.ProjectId == projectId).ToDictionaryAsync(document => document.ToolType);
            Check(saved.Count == expected.Count, $"{label} creates one record for each of the six tools");
            foreach (var (tool, value) in expected)
            {
                var document = saved[tool];
                Check(document.Id != Guid.Empty && document.ContentJson == value.Json && document.Revision == value.Revision,
                    $"{label} preserves {tool} content verbatim, including nulls, and its revision");
                Check(document.UpdatedByUserId is null && document.UpdatedAtUtc is null, $"{label} does not invent historical {tool} save metadata");
            }
            var oldColumns = await db.Database.SqlQueryRaw<int>("""
                SELECT COUNT(*) AS [Value] FROM sys.columns WHERE object_id = OBJECT_ID(N'dbo.Projects')
                AND name IN ('IdeasJson', 'IdeasRevision', 'ErdJson', 'ErdRevision', 'DesignJson', 'DesignRevision',
                    'RoadmapJson', 'RoadmapRevision', 'NotesJson', 'NotesRevision', 'WikiJson', 'WikiRevision')
                """).SingleAsync();
            Check(oldColumns == 0, $"{label} removes the twelve legacy document columns from Projects");
        }
    }

    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS: " + label);
    }
}
