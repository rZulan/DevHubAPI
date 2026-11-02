using System.Text.Json;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.ProjectTools;
using DevHub.Domain.Organizations;
using DevHub.Domain.Projects;
using DevHub.Domain.Users;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class OrganizationPermissionPersistenceChecks
{
    // Called inside the parent smoke test's rollback-only SQL transaction.
    public static async Task Run(IServiceProvider services, Project project, Dictionary<string, JsonElement> documents)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var repository = services.GetRequiredService<IOrganizationRepository>();
        var service = services.GetRequiredService<IProjectToolsService>();
        var organization = (await repository.GetByIdAsync(project.OrganizationId))!;
        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var user = User.Create($"settings-{suffix}@example.test", $"settings_{suffix}", "Settings", "Smoke", "test-only-hash", now);
        db.Users.Add(user);
        organization.AddMember(user.Id, now);
        // Only the role under test should grant access; real organizations may give
        // new members a default role with additional editor permissions.
        foreach (var inherited in organization.Members.Single(member => member.UserId == user.Id).RoleAssignments.ToArray())
            organization.RemoveRole(user.Id, inherited.RoleId);
        var role = organization.AddRole($"Settings {suffix}", "#38bdf8", [], now);
        organization.AssignRole(user.Id, role.Id, now);
        var team = organization.Teams.Single(team => team.Id == project.TeamId);
        team.AddMember(user.Id, now);
        await db.SaveChangesAsync();

        Check(await db.OrganizationRoles.AsNoTracking().AnyAsync(saved => saved.Id == role.Id), "New custom roles are inserted through the organization graph");
        var adjacent = organization.AddRole($"Adjacent {suffix}", "#a78bfa", [], now);
        await db.SaveChangesAsync();
        var originalPosition = role.Position;
        var adjacentPosition = adjacent.Position;
        Check(organization.MoveRole(organization.OwnerUserId, role.Id, 1, now), "Owner can reorder adjacent custom roles");
        await db.SaveChangesAsync();
        var savedPositions = await db.OrganizationRoles.AsNoTracking().Where(saved => saved.Id == role.Id || saved.Id == adjacent.Id).ToDictionaryAsync(saved => saved.Id, saved => saved.Position);
        Check(savedPositions[role.Id] == adjacentPosition && savedPositions[adjacent.Id] == originalPosition, "Both role positions persist together through SQL");

        var permissions = new Dictionary<string, string>
        {
            ["calendar"] = OrganizationPermissions.ManageTasks,
            ["erd"] = OrganizationPermissions.EditErd,
            ["ui-editor"] = OrganizationPermissions.EditUiDesigns,
            ["uml"] = OrganizationPermissions.EditUmlDiagrams,
            ["notes"] = OrganizationPermissions.ManageNotes,
            ["wiki"] = OrganizationPermissions.ManageWiki,
            ["sdlc"] = OrganizationPermissions.ManageRoadmap
        };
        foreach (var (allowedTool, permission) in permissions)
        {
            role.Update(role.Name, role.Color, role.Position, [permission], now);
            await db.SaveChangesAsync();
            foreach (var (tool, document) in documents)
            {
                var loaded = await service.Get(project.OrganizationId, project.Id, user.Id, tool, default);
                Check(loaded.IsSuccess, $"{permission} retains team project read access");
                var saved = await service.Save(project.OrganizationId, project.Id, user.Id, tool, new(loaded.Value!.Revision, document), default);
                Check(permissions[tool] == permission ? saved.IsSuccess : saved.Error?.Type == ErrorType.Forbidden,
                    $"{permission} {(permissions[tool] == permission ? "permits" : "does not permit")} {tool} writes through SQL");
            }
        }
        team.RemoveMember(user.Id);
        await db.SaveChangesAsync();
        Check((await service.Get(project.OrganizationId, project.Id, user.Id, "sdlc", default)).Error?.Type == ErrorType.NotFound,
            "Roadmap editing permission does not expose another team's project");

        var own = OrganizationInvite.Create(organization.Id, user.Id, $"own-{suffix}", now.AddDays(1), now);
        var other = OrganizationInvite.Create(organization.Id, organization.OwnerUserId, $"other-{suffix}", now.AddDays(1), now);
        repository.AddInvite(own); repository.AddInvite(other);
        await db.SaveChangesAsync();
        var ownLinks = await repository.ListActiveInvitesAsync(organization.Id, user.Id, now);
        Check(ownLinks.Any(link => link.Id == own.Id) && ownLinks.All(link => link.CreatedByUserId == user.Id), "Invite repository filters links by creator");
        Check(await repository.GetInviteByIdAsync(Guid.NewGuid(), own.Id) is null, "Invite lookup is scoped to the organization");
        own.Revoke(now.AddSeconds(1));
        await db.SaveChangesAsync();
        Check(!(await repository.ListActiveInvitesAsync(organization.Id, user.Id, now.AddSeconds(2))).Any(link => link.Id == own.Id), "Revoked links disappear from active SQL queries");
        db.Entry(own).State = EntityState.Detached;
        Check((await repository.GetInviteByIdAsync(organization.Id, own.Id))!.IsExpired(now.AddSeconds(2)), "Invite revocation persists through SQL");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
        Console.WriteLine("PASS: " + message);
    }
}
