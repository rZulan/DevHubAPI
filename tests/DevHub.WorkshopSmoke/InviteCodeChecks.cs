using System.Security.Cryptography;
using System.Text;
using DevHub.Application;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Organizations;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

internal static class InviteCodeChecks
{
    public static async Task Run()
    {
        static void Check(bool condition, string label) {if (!condition) throw new Exception(label); Console.WriteLine($"PASS invite codes: {label}");}
        static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
        var clock = new TestClock(); var now = clock.GetUtcNow();
        var owner = Guid.NewGuid(); var member = Guid.NewGuid(); var creator = Guid.NewGuid();
        var organization = Organization.Create("Code checks", null, owner, now);
        organization.AddMember(member, now); organization.AddMember(creator, now);
        var role = organization.AddRole("Inviter", "#abcdef", [OrganizationPermissions.CreateInvites], now);
        organization.AssignRole(creator, role.Id, now);
        var joiningUser = User.Create("joining@example.test", "code_joiner", "Joining", "User", "test-hash", now);
        var invites = new List<OrganizationInvite>(); var writes = 0;
        var repository = Stub.Create<IOrganizationRepository>((method, args) =>
        {
            if (method.Name == "GetByIdAsync") return Task.FromResult((Guid)args![0]! == organization.Id ? organization : null);
            if (method.Name == "GetInviteByTokenHashAsync") return Task.FromResult(invites.SingleOrDefault(invite => invite.TokenHash == (string)args![0]!));
            if (method.Name == "AddInvite") {invites.Add((OrganizationInvite)args![0]!); return null;}
            throw new NotSupportedException(method.Name);
        });
        var services = new ServiceCollection(); services.AddLogging(); services.AddApplication(); services.AddSingleton<TimeProvider>(clock); services.AddSingleton(repository);
        // Code-format tests isolate persistence; quota enforcement has its own disposable-SQL suite.
        services.AddSingleton(new DevHub.Application.Workshops.WorkshopLimits());
        services.AddSingleton(Stub.Create<DevHub.Application.Workshops.IWorkshopUsage>((method, _) => method.Name == "Reserve"
            ? Task.FromResult<DevHub.Application.Common.Error?>(null) : Task.FromResult(0)));
        services.AddSingleton(Stub.Create<IUnitOfWork>((_, _) => {writes++; return Task.FromResult(1);}));
        services.AddSingleton(Stub.Create<IUserRepository>((_, args) => Task.FromResult((Guid)args![0]! == joiningUser.Id ? joiningUser : null)));
        await using var provider = services.BuildServiceProvider(); var sender = provider.GetRequiredService<ISender>();
        Check(!(await sender.Send(new CreateOrganizationInviteCommand(organization.Id, member))).IsSuccess && writes == 0, "ordinary members cannot create invitations");
        var codes = new List<string>();
        for (var index = 0; index < 20; index++)
        {
            var result = await sender.Send(new CreateOrganizationInviteCommand(organization.Id, owner));
            if (!result.IsSuccess) throw new Exception("Invitation creation failed");
            codes.Add(result.Value!.Token);
        }
        Check(codes.Distinct().Count() == codes.Count && codes.All(code => System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-HJ-NP-Z2-9]{12}$")), "new invitations use distinct, readable 12-character codes");
        Check(invites.All(invite => invite.TokenHash.Length == 64) && invites[0].TokenHash == Hash(codes[0]), "only hashes of the generated codes are stored");
        Check(invites.All(invite => invite.ExpiresAtUtc == now.AddDays(7)), "seven-day expiry retained");
        Check((await sender.Send(new AcceptOrganizationInviteCommand($" {codes[0].ToLowerInvariant()} ", joiningUser.Id))).IsSuccess && organization.HasMember(joiningUser.Id), "lowercase codes and surrounding whitespace join the correct organization");
        var before = writes;
        Check((await sender.Send(new AcceptOrganizationInviteCommand(codes[0], joiningUser.Id))).IsSuccess && writes == before, "joining twice is idempotent");
        var legacy = new string('b', 64);
        invites.Add(OrganizationInvite.Create(organization.Id, owner, Hash(legacy), now.AddDays(7), now));
        Check((await sender.Send(new AcceptOrganizationInviteCommand(legacy.ToUpperInvariant(), joiningUser.Id))).IsSuccess, "existing long tokens remain valid");
        invites[0].Revoke(now);
        Check(!(await sender.Send(new AcceptOrganizationInviteCommand(codes[0], joiningUser.Id))).IsSuccess, "revoked short codes cannot be accepted");
        foreach (var invalid in new[] {"", "short", Guid.NewGuid().ToString(), "AAAAAAAAAAAA", new string('x', 1000)})
            Check(!(await sender.Send(new AcceptOrganizationInviteCommand(invalid, joiningUser.Id))).IsSuccess, "invalid or unknown code cannot grant membership");
        var delegated = await sender.Send(new CreateOrganizationInviteCommand(organization.Id, creator));
        Check(delegated.IsSuccess, "delegated invite creators can generate codes");
        role.Update(role.Name, role.Color, role.Position, [], now);
        Check(!(await sender.Send(new AcceptOrganizationInviteCommand(delegated.Value!.Token, joiningUser.Id))).IsSuccess, "codes stop working when their creator loses invite access");
        clock.Advance(TimeSpan.FromDays(8));
        Check(!(await sender.Send(new AcceptOrganizationInviteCommand(codes[1], joiningUser.Id))).IsSuccess, "expired short codes cannot be accepted");
    }
}
