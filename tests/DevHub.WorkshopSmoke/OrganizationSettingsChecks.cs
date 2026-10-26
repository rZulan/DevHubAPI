using DevHub.Application;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Organizations;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

internal static class OrganizationSettingsChecks
{
    public static async Task Run()
    {
        static void Check(bool condition, string label) {if (!condition) throw new Exception(label); Console.WriteLine($"PASS settings: {label}");}
        var now = DateTimeOffset.UtcNow;
        var owner = Guid.NewGuid(); var manager = Guid.NewGuid(); var member = Guid.NewGuid(); var outsider = Guid.NewGuid();
        var organization = Organization.Create("Settings test", "Test", owner, now);
        organization.AddMember(manager, now); organization.AddMember(member, now);
        var managerRole = organization.AddRole("Manager", "#abcdef", [OrganizationPermissions.ManageRoles, OrganizationPermissions.CreateInvites], now);
        var targetRole = organization.AddRole("Designer", "#aabbcc", [OrganizationPermissions.EditUiDesigns], now);
        organization.AssignRole(manager, managerRole.Id, now);
        foreach (var permission in OrganizationPermissions.All.Where(p => p.StartsWith("Edit ") || p is OrganizationPermissions.ManageNotes or OrganizationPermissions.ManageRoadmap or OrganizationPermissions.ManageDashboard))
        {
            var role = OrganizationRole.Create(organization.Id,"Narrow","#aabbcc",2,[permission],now);
            Check(role.HasPermission(permission) && !role.HasPermission(OrganizationPermissions.ViewAllProjects) && !role.HasPermission(OrganizationPermissions.Administrator), $"{permission} does not grant visibility or admin");
        }
        Check(OrganizationPermissions.Grants([OrganizationPermissions.EditIdeation],OrganizationPermissions.EditErd) && OrganizationPermissions.Grants([OrganizationPermissions.EditIdeation],OrganizationPermissions.ManageNotes),"legacy project editing retained");
        Check(OrganizationPermissions.Grants([OrganizationPermissions.ManageProjects],OrganizationPermissions.ManageRoadmap) && !OrganizationPermissions.Grants([OrganizationPermissions.ManageRoadmap],OrganizationPermissions.ManageProjects),"roadmap access can be delegated without project management");
        Check(OrganizationPermissions.Grants([OrganizationPermissions.ManageOrganization],OrganizationPermissions.ManageDashboard),"existing dashboard access retained");
        Check(!organization.CanManageMember(manager,member) && organization.CanAssignRole(manager,member,targetRole),"role assignment and member removal use separate permissions");
        Check(!organization.CanAssignRole(manager,outsider,targetRole),"roles cannot be assigned to nonmembers");
        Check(!organization.CanGrantPermissions(manager,[OrganizationPermissions.ManageNotes]),"cannot grant permissions the actor lacks");
        var positions = (managerRole.Position,targetRole.Position);
        Check(!organization.MoveRole(manager,targetRole.Id,-1,now),"cannot reorder past own or higher role");
        Check(organization.MoveRole(owner,targetRole.Id,-1,now) && managerRole.Position == positions.Item2 && targetRole.Position == positions.Item1,"role order swap preserves both positions");
        Check(targetRole.HasPermission(OrganizationPermissions.EditUiDesigns) && !targetRole.HasPermission(OrganizationPermissions.ManageNotes),"reordering preserves grants");
        var invites = new List<OrganizationInvite> {OrganizationInvite.Create(organization.Id,owner,"owner-hash",now.AddDays(7),now), OrganizationInvite.Create(organization.Id,manager,"manager-hash",now.AddDays(7),now)};
        var saves = 0;
        var repository = Stub.Create<IOrganizationRepository>((method,args) => method.Name switch
        {
            "GetByIdAsync" => Task.FromResult((Guid)args![0]! == organization.Id ? organization : null),
            "ListActiveInvitesAsync" => Task.FromResult<IReadOnlyList<OrganizationInvite>>(invites.Where(invite => invite.OrganizationId == (Guid)args![0]! && (args[1] is null || invite.CreatedByUserId == (Guid)args[1]!) && !invite.IsExpired((DateTimeOffset)args[2]!)).ToArray()),
            "GetInviteByIdAsync" => Task.FromResult(invites.SingleOrDefault(invite => invite.OrganizationId == (Guid)args![0]! && invite.Id == (Guid)args[1]!)),
            "GetInviteByTokenHashAsync" => Task.FromResult(invites.FirstOrDefault()),
            _ => throw new NotSupportedException(method.Name)
        });
        var services = new ServiceCollection(); services.AddLogging(); services.AddApplication(); services.AddSingleton(repository); services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Stub.Create<IUnitOfWork>((_,_) => {saves++;return Task.FromResult(1);}));
        var joiningUser = User.Create("joining@example.test","joininguser","Test","User","test-hash",now);
        services.AddSingleton(Stub.Create<IUserRepository>((_,_) => Task.FromResult<User?>(joiningUser)));
        await using var provider = services.BuildServiceProvider(); var sender = provider.GetRequiredService<ISender>();
        var own = await sender.Send(new ListOrganizationInvitesQuery(organization.Id,manager));
        Check(own.IsSuccess && own.Value!.Count == 1 && own.Value[0].CreatedByUserId == manager,"invite creators see only their own links");
        Check(!(await sender.Send(new RevokeOrganizationInviteCommand(organization.Id,invites[0].Id,manager))).IsSuccess && saves == 0,"creators cannot revoke another creator's link");
        Check(!(await sender.Send(new ListOrganizationInvitesQuery(organization.Id,outsider))).IsSuccess,"outsiders cannot list invitations");
        Check(!(await sender.Send(new RevokeOrganizationInviteCommand(Guid.NewGuid(),invites[0].Id,owner))).IsSuccess,"invite revocation is organization scoped");
        Check((await sender.Send(new RevokeOrganizationInviteCommand(organization.Id,invites[1].Id,manager))).IsSuccess && invites[1].IsExpired(DateTimeOffset.UtcNow),"creator can revoke own link");
        var all = await sender.Send(new ListOrganizationInvitesQuery(organization.Id,owner));
        Check(all.IsSuccess && all.Value!.Count == 1,"owner sees remaining active invitations");
        Check(!System.Text.Json.JsonSerializer.Serialize(all.Value).Contains("owner-hash"),"invite metadata never exposes token hashes");
        Check((await sender.Send(new RevokeOrganizationInviteCommand(organization.Id,invites[0].Id,owner))).IsSuccess,"owner can revoke any active link");
        Check(!(await sender.Send(new AcceptOrganizationInviteCommand("anything",joiningUser.Id))).IsSuccess,"revoked links cannot be accepted");
        invites[0] = OrganizationInvite.Create(organization.Id,member,"new-hash",now.AddDays(7),now);
        Check(!(await sender.Send(new AcceptOrganizationInviteCommand("anything",joiningUser.Id))).IsSuccess,"links from creators without invite access are invalid");
        managerRole.Update(managerRole.Name, managerRole.Color, managerRole.Position, [OrganizationPermissions.ManageInvites], now);
        invites[0] = OrganizationInvite.Create(organization.Id,owner,"managed-hash",now.AddDays(7),now);
        Check(organization.HasPermission(manager, OrganizationPermissions.CreateInvites), "Manage invites includes invitation creation");
        var managed = await sender.Send(new ListOrganizationInvitesQuery(organization.Id,manager));
        Check(managed.IsSuccess && managed.Value!.Count == 1 && managed.Value[0].CreatedByUserId == owner, "delegated invite manager can list other creators' links");
        Check((await sender.Send(new RevokeOrganizationInviteCommand(organization.Id,invites[0].Id,manager))).IsSuccess,"delegated invite manager can revoke another creator's link");
    }
}
