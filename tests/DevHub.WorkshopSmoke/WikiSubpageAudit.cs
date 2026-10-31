using System.Reflection;
using System.Text.Json;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class WikiSubpageAudit
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
            var parents = document.RootElement.GetProperty("pages").EnumerateArray().Select(page => page.TryGetProperty("parentId", out var parent) && parent.ValueKind == JsonValueKind.String ? parent.GetString() : null);
            overLimit += parents.Where(parent => parent is not null).GroupBy(parent => parent).Count(group => group.Count() > 2);
        }
        Console.WriteLine($"Wiki documents: {documents.Count}; parents exceeding two subpages: {overLimit}");
    }
}
