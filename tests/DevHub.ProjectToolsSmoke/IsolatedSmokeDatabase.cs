using DevHub.Domain.Organizations;
using DevHub.Domain.Projects;
using DevHub.Domain.Teams;
using DevHub.Domain.Users;
using DevHub.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;

internal sealed class IsolatedSmokeDatabase : IAsyncDisposable
{
    private readonly string connectionString;
    private readonly string databaseName = $"DevHubSmoke_{Guid.NewGuid():N}";
    public IsolatedSmokeDatabase(IConfiguration configuration)
    {
        var builder = new SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        if (!(builder.DataSource.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) || builder.DataSource.StartsWith(".\\", StringComparison.Ordinal)))
            throw new InvalidOperationException("Isolated SQL tests require a local SQL Server instance.");
        builder.InitialCatalog = databaseName;
        connectionString = builder.ConnectionString;
        configuration["ConnectionStrings:DefaultConnection"] = connectionString;
    }
    public async Task Seed(ApplicationDbContext db, string? legacyMigration = null, Func<ApplicationDbContext, Guid, Task>? verifyMigration = null)
    {
        if (legacyMigration is null) await db.Database.EnsureCreatedAsync();
        else await db.GetService<IMigrator>().MigrateAsync();
        var now = DateTimeOffset.UtcNow;
        var user = User.Create("owner@example.test", "smoke_owner", "Smoke", "Owner", "test-only-hash", now);
        var organization = Organization.Create("Settings SQL smoke", null, user.Id, now);
        var team = Team.Create(organization.Id, user.Id, "Smoke team", null, now);
        var project = Project.Create(organization.Id, team.Id, user.Id, "Smoke project", "Permission persistence", ProjectStatus.Planning,
            [], null, null, null, null, null, null, [], null, null, now);
        db.AddRange(user, organization, team, project);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        // Seed with the current entity model (including newer columns), then test the historical upgrade.
        if (legacyMigration is not null) await db.GetService<IMigrator>().MigrateAsync(legacyMigration);
        if (verifyMigration is not null) await verifyMigration(db, project.Id);
    }
    public async ValueTask DisposeAsync()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connectionString).Options;
        await using var db = new ApplicationDbContext(options);
        if (db.Database.GetDbConnection().Database != databaseName || !databaseName.StartsWith("DevHubSmoke_", StringComparison.Ordinal))
            throw new InvalidOperationException("Refusing to remove an unexpected database.");
        await db.Database.EnsureDeletedAsync();
        Console.WriteLine("Disposable local smoke database removed.");
    }
}
