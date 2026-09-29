using System.Reflection;
using DevHub.Application.Appearance;
using DevHub.Application;
using DevHub.Application.Common;
using DevHub.Application.Organizations;
using DevHub.Domain.Organizations;
using DevHub.Domain.Users;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MediatR;

// Run from api/: dotnet run --project tests/DevHub.AppearanceSmoke -c Release
// Real database writes are isolated in a transaction and always rolled back.
var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
var services = new ServiceCollection();
services.AddLogging();
services.AddApplication();
services.AddInfrastructure(configuration);
services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(
    configuration.GetConnectionString("DefaultConnection"),
    sql => sql.ExecutionStrategy(dependencies => new Microsoft.EntityFrameworkCore.Storage.NonRetryingExecutionStrategy(dependencies))));
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var service = scope.ServiceProvider.GetRequiredService<IMemberAppearanceService>();
var sender = scope.ServiceProvider.GetRequiredService<ISender>();
await using var transaction = await db.Database.BeginTransactionAsync();
try
{
    var ct = CancellationToken.None;
    var organization = await db.Organizations.Include(candidate => candidate.Members).Include(candidate => candidate.Roles).FirstAsync(ct);
    var org = organization.Id;
    var owner = organization.Members.First(member => member.IsOwner).UserId;

    // A regular member with only the default role and no permissions.
    var member = User.Create($"appearance-smoke-{Guid.NewGuid():N}@example.com", $"smoke{Guid.NewGuid():N}"[..20], "Smoke", "Member", "hash", DateTimeOffset.UtcNow);
    db.Users.Add(member);
    organization.AddMember(member.Id, DateTimeOffset.UtcNow);
    await db.SaveChangesAsync(ct);
    var deniedNickname = await sender.Send(new ChangeOrganizationMemberNicknameCommand(org, owner, member.Id, "Nope"), ct);
    Check(!deniedNickname.IsSuccess && deniedNickname.Error?.Type == ErrorType.Forbidden, "Members without Change nicknames permission cannot rename others");
    Require(await sender.Send(new ChangeOrganizationMemberNicknameCommand(org, member.Id, owner, "Workshop Alias"), ct));
    db.ChangeTracker.Clear();
    var nicknamedOrganization = await db.Organizations.Include(candidate => candidate.Members).SingleAsync(candidate => candidate.Id == org, ct);
    Check(nicknamedOrganization.Members.Single(candidate => candidate.UserId == member.Id).Nickname == "Workshop Alias", "Authorized nickname persists per organization");
    Require(await sender.Send(new ChangeOrganizationMemberNicknameCommand(org, member.Id, owner, "  "), ct));
    db.ChangeTracker.Clear();
    Check((await db.Organizations.Include(candidate => candidate.Members).SingleAsync(candidate => candidate.Id == org, ct)).Members.Single(candidate => candidate.UserId == member.Id).Nickname is null, "Blank nickname restores the profile name");
    await db.Set<OrganizationMember>().Where(candidate => candidate.OrganizationId == org && candidate.UserId == owner)
        .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.ColorSchemeId, OrganizationColorScheme.DefaultId), ct);
    db.ChangeTracker.Clear();

    var initial = Require(await service.Get(org, member.Id, ct));
    Check(initial.ActiveSchemeId == "default" && initial.Presets.Count == 5 && initial.CustomSchemes.Count == 0, "New members start on the default preset");
    Check(initial.Presets.All(preset => preset.IsPreset && preset.Light.IsValid() && preset.Dark.IsValid()), "Every preset defines valid light and dark palettes");
    Check((await service.Get(org, Guid.NewGuid(), ct)).Error?.Type == ErrorType.NotFound, "Non-members cannot read appearance");
    Check((await service.SetActive(org, Guid.NewGuid(), new("ocean"), ct)).Error?.Type == ErrorType.NotFound, "Non-members cannot choose a scheme");

    Check(Require(await service.SetActive(org, member.Id, new("OCEAN"), ct)).ActiveSchemeId == "ocean", "Members without any permission can choose a scheme");
    Check(Require(await service.Get(org, owner, ct)).ActiveSchemeId == "default", "One member's choice does not change anyone else's");
    Check(Require(await service.SetActive(org, member.Id, new("forest"), ct)).ActiveSchemeId == "forest", "Switching again immediately is allowed");
    Check((await service.SetActive(org, member.Id, new("neon"), ct)).Error?.Type == ErrorType.NotFound, "Unknown schemes cannot be chosen");

    Check((await service.Create(org, member.Id, Save("Bad", accent: "red"), ct)).Error?.Type == ErrorType.Validation, "Non-hex colors are rejected");
    Check((await service.Create(org, member.Id, Save(new string('x', 41)), ct)).Error?.Type == ErrorType.Validation, "Overlong names are rejected");
    Check((await service.Create(org, member.Id, Save("ocean"), ct)).Error?.Type == ErrorType.Conflict, "Custom schemes cannot reuse preset names");
    var mine = Require(await service.Create(org, member.Id, Save("Smoke Brand", accent: "#ABCDEF"), ct));
    Check(!mine.IsPreset && mine.Light.Accent == "#abcdef", "Members can create custom schemes with normalized colors");
    Check((await service.Create(org, member.Id, Save("smoke brand"), ct)).Error?.Type == ErrorType.Conflict, "A member's scheme names are unique");
    var ownerCopy = Require(await service.Create(org, owner, Save("Smoke Brand"), ct));
    Check(ownerCopy.Id != mine.Id, "Different members can use the same scheme name");

    var mineId = Guid.Parse(mine.Id);
    Check(Require(await service.Get(org, owner, ct)).CustomSchemes.All(scheme => scheme.Id != mine.Id), "Custom schemes are private to their creator");
    Check((await service.SetActive(org, owner, new(mine.Id), ct)).Error?.Type == ErrorType.NotFound, "Members cannot use someone else's scheme");
    Check((await service.Update(org, owner, mineId, Save("Hijack"), ct)).Error?.Type == ErrorType.NotFound, "Members cannot edit someone else's scheme");
    Check((await service.Delete(org, owner, mineId, ct)).Error?.Type == ErrorType.NotFound, "Members cannot delete someone else's scheme");

    Check(Require(await service.SetActive(org, member.Id, new(mine.Id), ct)).ActiveSchemeId == mine.Id, "Members can use their own custom scheme");
    Require(await service.Update(org, member.Id, mineId, Save("Renamed Brand", accent: "#123456"), ct));
    db.ChangeTracker.Clear();
    var updated = Require(await service.Get(org, member.Id, ct));
    Check(updated.CustomSchemes.Single().Name == "Renamed Brand" && updated.CustomSchemes.Single().Light.Accent == "#123456", "Editing the scheme in use persists immediately");
    Check(Require(await service.Delete(org, member.Id, mineId, ct)), "The scheme in use can be deleted");
    Check(Require(await service.Get(org, member.Id, ct)).ActiveSchemeId == "default", "Deleting the scheme in use returns the member to the default");
    Check((await service.Delete(org, member.Id, mineId, ct)).Error?.Type == ErrorType.NotFound, "Deleting twice reports a missing scheme");

    await db.Set<OrganizationMember>().Where(candidate => candidate.OrganizationId == org && candidate.UserId == member.Id)
        .ExecuteUpdateAsync(update => update.SetProperty(candidate => candidate.ColorSchemeId, Guid.NewGuid().ToString()), ct);
    Check(Require(await service.Get(org, member.Id, ct)).ActiveSchemeId == "default", "A dangling scheme choice falls back to the default preset");

    Require(await service.Create(org, member.Id, Save("Leaving Soon"), ct));
    db.ChangeTracker.Clear();
    var reloaded = await db.Organizations.Include(candidate => candidate.Members).ThenInclude(candidate => candidate.RoleAssignments)
        .Include(candidate => candidate.Teams).SingleAsync(candidate => candidate.Id == org, ct);
    Check(reloaded.RemoveMember(member.Id, DateTimeOffset.UtcNow), "Test member can leave");
    await db.SaveChangesAsync(ct);
    Check(!await db.OrganizationColorSchemes.AnyAsync(scheme => scheme.OrganizationId == org && scheme.UserId == member.Id, ct), "Leaving an organization removes the member's schemes");

    db.ChangeTracker.Clear();
    db.Organizations.Remove(await db.Organizations.SingleAsync(candidate => candidate.Id == org, ct));
    await db.SaveChangesAsync(ct);
    Check(!await db.OrganizationColorSchemes.AnyAsync(scheme => scheme.OrganizationId == org, ct), "Deleting an organization satisfies every foreign key and cascades its color schemes");
}
finally { await transaction.RollbackAsync(); Console.WriteLine("All test writes rolled back."); }

static SaveColorSchemeRequest Save(string name, string accent = "#2563eb") =>
    new(name, new(accent, "#ffffff", "#ffffff", "#f4f4f5", "#09090b"), new(accent, "#09090b", "#18181b", "#18181b", "#fafafa"));
static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new Exception(result.Error!.Code);
static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine($"PASS: {label}"); }
