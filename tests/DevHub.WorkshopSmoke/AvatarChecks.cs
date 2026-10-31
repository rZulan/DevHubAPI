using DevHub.Application;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Organizations;
using DevHub.Application.Teams;
using DevHub.Domain.Common;
using DevHub.Domain.Organizations;
using DevHub.Domain.Teams;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

internal static class AvatarChecks
{
    public static async Task Run()
    {
        static void Check(bool condition, string label) {if (!condition) throw new Exception(label); Console.WriteLine($"PASS avatars: {label}");}
        const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j1ioAAAAASUVORK5CYII=";
        Check(AvatarImage.IsValid(png) && AvatarImage.IsValid(null), "raster avatar and removal accepted");
        foreach (var invalid in new[] {"", "https://example.test/avatar.png", "data:image/svg+xml;base64,PHN2Zy8+", "data:image/png;base64,not-base64", "data:image/jpeg;base64," + png.Split(',')[1], new string('a', AvatarImage.MaxLength + 1)})
            Check(!AvatarImage.IsValid(invalid), "invalid, active or oversized avatar rejected");
        var bytes = new byte[AvatarImage.MaxBytes + 1]; Convert.FromBase64String(png.Split(',')[1]).CopyTo(bytes,0);
        Check(!AvatarImage.IsValid("data:image/png;base64," + Convert.ToBase64String(bytes)), "decoded payload limit enforced");
        var now = DateTimeOffset.UtcNow;
        var owner = Guid.NewGuid(); var manager = Guid.NewGuid(); var leader = Guid.NewGuid(); var member = Guid.NewGuid(); var outsider = Guid.NewGuid();
        var org = Organization.Create("Avatar checks", "Profile", owner, now);
        foreach (var id in new[] {manager,leader,member}) org.AddMember(id,now);
        var role = org.AddRole("Identity manager", "#aabbcc", [OrganizationPermissions.ManageOrganization, OrganizationPermissions.ManageTeams], now);
        org.AssignRole(manager,role.Id,now);
        var team = Team.Create(org.Id,leader,"Design","Team",now);
        var saves = 0;
        var services = new ServiceCollection(); services.AddLogging(); services.AddApplication(); services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Stub.Create<IOrganizationRepository>((method,args) => method.Name == "GetByIdAsync" ? Task.FromResult((Guid)args![0]! == org.Id ? org : null) : throw new NotSupportedException(method.Name)));
        services.AddSingleton(Stub.Create<ITeamRepository>((method,args) => method.Name == "GetByIdAsync" ? Task.FromResult((Guid)args![0]! == org.Id && (Guid)args[1]! == team.Id ? team : null) : throw new NotSupportedException(method.Name)));
        services.AddSingleton(Stub.Create<IUnitOfWork>((_,_) => {saves++; return Task.FromResult(1);}));
        await using var provider = services.BuildServiceProvider(); var sender = provider.GetRequiredService<ISender>();
        var saved = await sender.Send(new SetOrganizationAvatarCommand(org.Id,owner,png));
        Check(saved.IsSuccess && saved.Value!.AvatarUrl == png && org.AvatarUrl == png, "organization owner can save and read its icon");
        await sender.Send(new SetOrganizationAvatarCommand(org.Id,owner,png));
        Check(saves == 1, "repeated upload is idempotent");
        Check(!(await sender.Send(new SetOrganizationAvatarCommand(org.Id,member,null))).IsSuccess && org.AvatarUrl == png, "ordinary member cannot remove organization icon");
        Check(!(await sender.Send(new SetOrganizationAvatarCommand(org.Id,outsider,png))).IsSuccess, "outsider cannot change an organization icon");
        Check(!(await sender.Send(new SetOrganizationAvatarCommand(Guid.NewGuid(),owner,png))).IsSuccess, "organization icon is scoped to the requested organization");
        Check(!(await sender.Send(new SetOrganizationAvatarCommand(org.Id,owner,"invalid"))).IsSuccess && saves == 1, "invalid upload does not write");
        Check((await sender.Send(new SetOrganizationAvatarCommand(org.Id,manager,null))).IsSuccess && org.AvatarUrl is null, "delegated organization manager can remove icon");
        var teamSaved = await sender.Send(new SetTeamAvatarCommand(org.Id,team.Id,leader,png));
        Check(teamSaved.IsSuccess && teamSaved.Value!.AvatarUrl == png && team.AvatarUrl == png, "team leader can save team icon");
        Check(!(await sender.Send(new SetTeamAvatarCommand(org.Id,team.Id,member,null))).IsSuccess && team.AvatarUrl == png, "ordinary member cannot remove team icon");
        Check(!(await sender.Send(new SetTeamAvatarCommand(org.Id,team.Id,outsider,null))).IsSuccess, "outsider cannot edit team icon");
        Check(!(await sender.Send(new SetTeamAvatarCommand(org.Id,Guid.NewGuid(),owner,png))).IsSuccess, "team icon cannot target another team");
        team.Update("Renamed team","Updated details",leader,now);
        Check(team.AvatarUrl == png, "editing team details retains its image");
        Check((await sender.Send(new SetTeamAvatarCommand(org.Id,team.Id,manager,null))).IsSuccess && team.AvatarUrl is null, "team manager can remove icon");
        await sender.Send(new SetOrganizationAvatarCommand(org.Id,owner,png));
        org.Update("Renamed organization", "Updated details",now);
        Check(org.AvatarUrl == png, "editing organization details retains its image");
    }
}
