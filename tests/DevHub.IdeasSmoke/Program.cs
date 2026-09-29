using System.Reflection;
using System.Text.Json;
using DevHub.Api.Realtime;
using DevHub.Application.Common;
using DevHub.Application.Ideas;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Run from api/: dotnet run --project tests/DevHub.IdeasSmoke -c Release
// Real database writes are isolated in a transaction and always rolled back.
var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
var services = new ServiceCollection();
services.AddLogging();
services.AddInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var selections = new IdeasSelectionTracker();
var selectionOrg = Guid.NewGuid();
var selectionProject = Guid.NewGuid();
var firstUser = Guid.NewGuid();
var secondUser = Guid.NewGuid();
Check(selections.Join(selectionOrg, selectionProject, firstUser, "first-1", "first_user").Count == 0, "First Ideas collaborator joins an empty selection session");
selections.Update("first-1", ["shape-a"]);
Check(selections.Join(selectionOrg, selectionProject, secondUser, "second-1", "second_user").Single().ShapeIds.SequenceEqual(["shape-a"]), "New collaborators receive current shape selections");
selections.Join(selectionOrg, selectionProject, firstUser, "first-2", "first_user");
selections.Update("first-2", ["shape-b"]);
Check(selections.Leave("first-1")!.Selection.ShapeIds.SequenceEqual(["shape-b"]), "Closing one tab preserves the member's other active selection");
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var service = scope.ServiceProvider.GetRequiredService<IIdeasService>();
await using var transaction = await db.Database.BeginTransactionAsync();
try
{
    var project = await db.Projects.AsNoTracking().FirstAsync();
    var org = project.OrganizationId;
    var owner = await db.Organizations.Where(o => o.Id == org).Select(o => o.OwnerUserId).SingleAsync();
    var ct = CancellationToken.None;
    Check(await service.CanView(org, project.Id, owner, ct), "Live subscription access accepts an authorized owner");
    Check(!await service.CanView(org, project.Id, Guid.NewGuid(), ct), "Live subscription access rejects a non-member");
    Check(!await service.CanView(Guid.NewGuid(), project.Id, owner, ct), "Live subscription access rejects another organization");
    var initial = Require(await service.Get(org, project.Id, owner, ct));
    const string shape = """
      {"id":"smoke-rectangle","kind":"rectangle","groupId":"smoke-group","x":-20,"y":30,"width":200,"height":100,"appearance":"fill","fillColor":"#abcdef","outlineColor":"#123456","cornerRadius":12,"outlineWidth":3,"outlineStyle":"dashed","text":"Saved text\nSecond line","noteHeader":"","noteBody":"","textAlign":"right","verticalAlign":"bottom","fontFamily":"Montserrat","fontSize":24,"bold":true,"italic":true,"underline":true,"strikethrough":true,"textColor":"auto","letterSpacing":1.5,"lineHeight":1.8,"textIndent":12,"noteHeaderStyle":{"fontFamily":"Poppins","fontSize":28,"bold":true,"italic":false,"underline":false,"strikethrough":false,"textColor":"#112233","letterSpacing":1,"lineHeight":1.2,"textIndent":0,"textAlign":"center"},"noteBodyStyle":{"fontFamily":"Roboto Mono","fontSize":14,"bold":false,"italic":true,"underline":false,"strikethrough":false,"textColor":"auto","letterSpacing":0.5,"lineHeight":1.6,"textIndent":10,"textAlign":"left"},"textRuns":[{"text":"Saved","bold":true,"italic":false,"underline":false,"strikethrough":false},{"text":" text\nSecond line","bold":false,"italic":true,"underline":false,"strikethrough":false}]}
      """;
    var items = JsonSerializer.Deserialize<JsonElement>("[" + shape + "," + shape.Replace("smoke-rectangle", "second").Replace("\"kind\":\"rectangle\"", "\"kind\":\"note\"").Replace("\"x\":-20", "\"x\":300").Replace("\"noteHeader\":\"\"", "\"noteHeader\":\"Note header\"").Replace("\"noteBody\":\"\"", "\"noteBody\":\"Note body\"") + "]");
    var saved = Require(await service.Save(org, project.Id, owner, new(initial.Revision, items), ct));
    Check(saved.Revision == initial.Revision + 1, "Save increments document revision");
    db.ChangeTracker.Clear();
    var loaded = Require(await service.Get(org, project.Id, owner, ct));
    Check(loaded.Items.GetRawText() == items.GetRawText(), "Shapes, notes, groups, order, geometry, text and formatting round trip through SQL Server");
    Check((await service.Save(org, project.Id, owner, new(initial.Revision, items), ct)).Error?.Type == ErrorType.Conflict, "Stale writes cannot overwrite newer changes");
    Check((await service.Get(org, project.Id, Guid.NewGuid(), ct)).Error?.Type == ErrorType.NotFound, "Non-members cannot read canvases");
    Check((await service.Save(org, project.Id, Guid.NewGuid(), new(saved.Revision, items), ct)).Error?.Type == ErrorType.NotFound, "Non-members cannot save canvases");
    Check((await service.Get(Guid.NewGuid(), project.Id, owner, ct)).Error?.Type == ErrorType.NotFound, "Cross-organization project access is rejected");
    var invalid = JsonSerializer.Deserialize<JsonElement>("[" + shape.Replace("\"width\":200", "\"width\":-1") + "]");
    Check((await service.Save(org, project.Id, owner, new(saved.Revision, invalid), ct)).Error?.Type == ErrorType.Validation, "Invalid shape geometry is rejected");
    var duplicate = JsonSerializer.Deserialize<JsonElement>("[" + shape + "," + shape + "]");
    Check((await service.Save(org, project.Id, owner, new(saved.Revision, duplicate), ct)).Error?.Type == ErrorType.Validation, "Duplicate shape IDs are rejected");
    var cleared = Require(await service.Save(org, project.Id, owner, new(saved.Revision, JsonSerializer.Deserialize<JsonElement>("[]")), ct));
    Check(Require(await service.Get(org, project.Id, owner, ct)).Items.GetArrayLength() == 0, "Deleting all shapes persists an empty document");
    Check(cleared.Revision == saved.Revision + 1, "Invalid and stale writes did not modify the revision");
}
finally { await transaction.RollbackAsync(); Console.WriteLine("All test writes rolled back."); }

static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new Exception(result.Error!.Code);
static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine($"PASS: {label}"); }
