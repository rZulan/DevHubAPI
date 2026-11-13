using System.Reflection;
using DevHub.Application;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Organizations;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using MediatR;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
await using var isolated = new IsolatedSmokeDatabase(configuration);
var services = new ServiceCollection(); services.AddLogging(); services.AddApplication(); services.AddInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();
Guid organizationId = default, ownerId = default, targetId = default, administratorId = default;
void Check(bool condition, string label) {if (!condition) throw new Exception(label); Console.WriteLine("PASS ownership: " + label);}
await using (var seed = provider.CreateAsyncScope())
{
    var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await isolated.Seed(db, "20261029054418_AddOrganizationAndTeamAvatars", async (context, _) =>
    {
        var organization = (await seed.ServiceProvider.GetRequiredService<IOrganizationRepository>().GetByIdAsync((await db.Organizations.SingleAsync()).Id))!;
        organizationId = organization.Id; ownerId = organization.OwnerUserId;
        var now = DateTimeOffset.UtcNow;
        var target = User.Create("next@example.test", "next_owner", "Next", "Owner", "test-only", now);
        var admin = User.Create("admin@example.test", "ownership_admin", "Admin", "Member", "test-only", now);
        targetId = target.Id; administratorId = admin.Id; db.Users.AddRange(target, admin);
        organization.AddMember(targetId, now); organization.AddMember(administratorId, now);
        var adminRole = organization.AddRole("Administrator", "#abcdef", [OrganizationPermissions.Administrator], now);
        var retained = organization.AddRole("Reporter", "#aabbcc", [OrganizationPermissions.ManageBugReports], now);
        organization.AssignRole(administratorId, adminRole.Id, now); organization.AssignRole(ownerId, retained.Id, now);
        var ownerRole = organization.Roles.Single(role => role.IsOwnerRole);
        Check(!organization.AssignRole(targetId, ownerRole.Id, now), "ordinary role assignment cannot create another owner");
        Check(!organization.TransferOwnership(administratorId, targetId, now) && !organization.TransferOwnership(ownerId, ownerId, now) && !organization.TransferOwnership(ownerId, Guid.NewGuid(), now), "domain rejects nonowner, self and nonmember transfers");
        Check(!organization.RemoveMember(ownerId, now), "the sole owner cannot leave or be removed");
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlAsync($"UPDATE OrganizationMembers SET IsOwner = 1 WHERE OrganizationId = {organizationId} AND UserId = {targetId}");
        await db.Database.ExecuteSqlAsync($"INSERT INTO OrganizationMemberRoles (OrganizationId, UserId, RoleId, AssignedAtUtc) VALUES ({organizationId}, {targetId}, {ownerRole.Id}, {now})");
        db.ChangeTracker.Clear();
        var malformed = (await seed.ServiceProvider.GetRequiredService<IOrganizationRepository>().GetByIdAsync(organizationId))!;
        Check(!malformed.IsOwner(targetId) && !malformed.HasPermission(targetId, OrganizationPermissions.Administrator), "stale owner flags and role assignments do not grant ownership authority");
        db.ChangeTracker.Clear();
        await db.GetService<IMigrator>().MigrateAsync();
        Check(await db.Set<OrganizationMember>().CountAsync(member => member.OrganizationId == organizationId && member.IsOwner) == 1, "migration removes duplicate owner flags");
        Check(await db.OrganizationMemberRoles.CountAsync(assignment => assignment.OrganizationId == organizationId && assignment.RoleId == ownerRole.Id) == 1, "migration removes duplicate owner-role assignments");
        Check((await db.Organizations.AsNoTracking().SingleAsync()).OwnerUserId == ownerId, "migration preserves the recorded owner");
        try
        {
            await db.Database.ExecuteSqlAsync($"UPDATE OrganizationMembers SET IsOwner = 1 WHERE OrganizationId = {organizationId} AND UserId = {targetId}");
            throw new Exception("Duplicate owner accepted by database");
        }
        catch (SqlException error) when (error.Number is 2601 or 2627) {Console.WriteLine("PASS ownership: database prevents a second owner");}
    });
}

async Task<Result<Unit>> Transfer(Guid actor, Guid target)
{
    await using var scope = provider.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new TransferOrganizationOwnershipCommand(organizationId, target, actor));
}
Check(!(await Transfer(administratorId, targetId)).IsSuccess, "Administrator cannot transfer an organization they do not own");
Check(!(await Transfer(Guid.NewGuid(), targetId)).IsSuccess && !(await Transfer(ownerId, Guid.NewGuid())).IsSuccess, "outsiders and nonmember targets are rejected");
Check(!(await Transfer(ownerId, ownerId)).IsSuccess, "self transfer is rejected");
Check((await Transfer(ownerId, targetId)).IsSuccess, "current owner can transfer ownership to an existing member");
await using (var read = provider.CreateAsyncScope())
{
    var org = (await read.ServiceProvider.GetRequiredService<IOrganizationRepository>().GetByIdAsync(organizationId))!;
    Check(org.OwnerUserId == targetId && org.Members.Count(member => member.IsOwner) == 1 && org.IsOwner(targetId), "owner ID and flag agree after transfer");
    Check(org.Members.Single(member => member.UserId == ownerId).RoleAssignments.All(assignment => org.Roles.Single(role => role.Id == assignment.RoleId).IsOwnerRole == false), "former owner loses the protected role");
    Check(org.HasMember(ownerId) && org.HasPermission(ownerId, OrganizationPermissions.ManageBugReports) && !org.HasPermission(ownerId, OrganizationPermissions.Administrator), "former owner remains a member and retains only their other roles");
}
Check(!(await Transfer(ownerId, administratorId)).IsSuccess, "former owner cannot transfer again using a stale session");
Check((await Transfer(targetId, ownerId)).IsSuccess, "new owner can transfer back");
var concurrent = await Task.WhenAll(Transfer(ownerId, targetId), Transfer(ownerId, administratorId));
Check(concurrent.Count(result => result.IsSuccess) == 1, "simultaneous transfers allow exactly one winner");
await using (var final = provider.CreateAsyncScope())
{
    var org = (await final.ServiceProvider.GetRequiredService<IOrganizationRepository>().GetByIdAsync(organizationId))!;
    Check(org.Members.Count(member => member.IsOwner) == 1 && org.Members.Single(member => member.IsOwner).UserId == org.OwnerUserId, "simultaneous transfers cannot produce multiple owners");
}
Console.WriteLine("Ownership migration and SQL checks completed; disposable database will be removed.");
