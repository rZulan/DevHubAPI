using System.Reflection;
using System.Text.Json;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class WikiDepthAudit
{
    public static async Task Run()
    {
        var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (!db.Database.GetDbConnection().DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Audit requires a local development database.");
        var documents = await db.ProjectDocuments.AsNoTracking().Where(document => document.ToolType == "wiki" && document.ContentJson != null).Select(document => document.ContentJson!).ToListAsync();
        var overLimit = 0;
        foreach (var json in documents)
        {
            using var document = JsonDocument.Parse(json);
            var parents = document.RootElement.GetProperty("pages").EnumerateArray().ToDictionary(page => page.GetProperty("id").GetString()!, page => page.TryGetProperty("parentId", out var parent) && parent.ValueKind == JsonValueKind.String ? parent.GetString() : null);
            foreach (var id in parents.Keys)
            {
                var seen = new HashSet<string> {id};
                var parent = parents[id];
                while (parent is not null)
                {
                    if (!seen.Add(parent) || !parents.TryGetValue(parent, out var next)) throw new InvalidOperationException("Malformed wiki hierarchy.");
                    parent = next;
                }
                if (seen.Count > 3) overLimit++;
            }
        }
        Console.WriteLine($"Wiki documents: {documents.Count}; pages exceeding two nested levels: {overLimit}");
    }
}
