using System.Reflection;
using System.Text.Json;
using DevHub.Infrastructure;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class OwnershipAudit
{
    public static async Task Run(string username)
    {
        var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
            .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
        var services = new ServiceCollection(); services.AddLogging(); services.AddInfrastructure(configuration);
        await using var provider = services.BuildServiceProvider(); await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source = db.Database.GetDbConnection().DataSource;
        if (!source.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) && !source.StartsWith(".\\", StringComparison.Ordinal)) throw new InvalidOperationException("Ownership audit requires a local development database.");
        var user = await db.Users.AsNoTracking().SingleAsync(user => user.NormalizedUsername == username.ToUpperInvariant());
        var organizations = await scope.ServiceProvider.GetRequiredService<IOrganizationRepository>().ListForUserAsync(user.Id);
        var names = await db.Users.AsNoTracking().Select(user => new {user.Id, user.Username}).ToDictionaryAsync(user => user.Id, user => user.Username);
        var own = organizations.Where(org => org.OwnerUserId == user.Id || org.Members.Any(member => member.UserId == user.Id && member.IsOwner));
        var invalidOrganizationCount = await db.Organizations.CountAsync(org => org.Members.Count(member => member.IsOwner) != 1 || !org.Members.Any(member => member.UserId == org.OwnerUserId && member.IsOwner));
        Console.WriteLine(JsonSerializer.Serialize(new {Username = user.Username, InvalidOrganizationCount = invalidOrganizationCount, Organizations = own.Select(org => new {
            org.Id, org.Name, Owner = names.GetValueOrDefault(org.OwnerUserId),
            FlaggedOwners = org.Members.Where(member => member.IsOwner).Select(member => names.GetValueOrDefault(member.UserId)),
            OwnerRoleHolders = org.Members.Where(member => member.RoleAssignments.Any(assignment => org.Roles.Any(role => role.Id == assignment.RoleId && role.IsOwnerRole))).Select(member => names.GetValueOrDefault(member.UserId))
        })}, new JsonSerializerOptions {WriteIndented = true}));
    }
}
