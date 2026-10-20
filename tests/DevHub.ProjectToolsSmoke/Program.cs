using System.Reflection;
using System.Text.Json;
using DevHub.Application.Common;
using DevHub.Application.ProjectTools;
using DevHub.Infrastructure;
using DevHub.Infrastructure.ProjectTools;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Run from api/: dotnet run --project tests/DevHub.ProjectToolsSmoke -c Release
// All real database writes are rolled back, including on test failures.
ErdSchemaChecks.Run();
NotesChecks.Run();
if (args.Contains("--schema-only")) return;
var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
var services = new ServiceCollection(); services.AddLogging(); services.AddInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var service = scope.ServiceProvider.GetRequiredService<IProjectToolsService>();
await CollaborationChecks.Run();
JsonElement Json(string json) => JsonSerializer.Deserialize<JsonElement>(json);
var documents = new Dictionary<string, JsonElement>
{
    ["erd"] = Json("""
    {"tables":[{"id":"t1","name":"users","x":10,"y":20,"fields":[{"id":"f1","name":"id","type":"UUID","length":"","primary":true,"nullable":false,"unique":false,"defaultValue":""}]},{"id":"t2","name":"orders","x":400,"y":200,"fields":[{"id":"f2","name":"user_id","type":"UUID","length":"","primary":false,"nullable":false,"unique":false,"defaultValue":""}]}],"relationships":[{"id":"r1","fromTable":"t1","fromField":"f1","toTable":"t2","toField":"f2","cardinality":"1:N","onDelete":"CASCADE"}]}
    """),
    ["ui-editor"] = Json("""
    {"layers":[{"id":"frame","name":"Mobile","kind":"frame","x":10,"y":20,"width":390,"height":844,"fill":"#ffffff","stroke":"#000000","strokeWidth":1,"radius":0,"opacity":1,"text":"","fontSize":16,"textColor":"#000000","hidden":false,"locked":false},{"id":"button","parentId":"frame","name":"Action","kind":"button","x":30,"y":60,"width":180,"height":48,"fill":"#8b5cf6","stroke":"#000000","strokeWidth":0,"radius":12,"opacity":0.8,"text":"Continue","fontSize":16,"textColor":"#ffffff","hidden":false,"locked":false}]}
    """),
    ["sdlc"] = Json("""{"model":"waterfall","currentPhase":2,"completed":false,"notes":{"implementation":"Building the project"}}"""),
    ["notes"] = Json("""{"notes":[{"id":"note-1","title":"Next release","body":"Review onboarding","color":"#f6e7a6","updatedAt":"2026-10-20T00:00:00.000Z"}]}""")
};
foreach (var (tool, document) in documents) Check(ToolDocumentValidation.Valid(tool, document), $"Valid {tool} document accepted");
Check(!ToolDocumentValidation.Valid("erd", Json(documents["erd"].GetRawText().Replace("\"toField\":\"f2\"", "\"toField\":\"missing\""))), "Dangling ERD reference rejected");
var optionalEnds = Json(documents["erd"].GetRawText().Replace("\"cardinality\":\"1:N\"", "\"cardinality\":\"1:N\",\"leftEnd\":\"zero-one\",\"rightEnd\":\"zero-many\""));
Check(ToolDocumentValidation.Valid("erd", optionalEnds), "Custom optional endpoint symbols accepted");
Check(!ToolDocumentValidation.Valid("erd", Json(optionalEnds.GetRawText().Replace("\"zero-many\"", "\"one\""))), "Cardinality and endpoint disagreement rejected");
Check(!ToolDocumentValidation.Valid("erd", Json(optionalEnds.GetRawText().Replace("\"zero-one\"", "\"invalid\""))), "Unknown endpoint symbols rejected");
var reversed = Json(documents["erd"].GetRawText().Replace("\"fromTable\":\"t1\",\"fromField\":\"f1\",\"toTable\":\"t2\",\"toField\":\"f2\",\"cardinality\":\"1:N\"", "\"fromTable\":\"t2\",\"fromField\":\"f2\",\"toTable\":\"t1\",\"toField\":\"f1\",\"cardinality\":\"N:1\",\"leftEnd\":\"many\",\"rightEnd\":\"one\""));
Check(ToolDocumentValidation.Valid("erd", reversed), "Many-to-one validates the referenced key on the right");
Check(!ToolDocumentValidation.Valid("ui-editor", Json(documents["ui-editor"].GetRawText().Replace("\"parentId\":\"frame\"", "\"parentId\":\"missing\""))), "Missing parent frame rejected");
Check(!ToolDocumentValidation.Valid("sdlc", Json("""{"model":"agile","currentPhase":0,"completed":false,"notes":{}}""")), "Unsupported SDLC model rejected");
Check(!ToolDocumentValidation.Valid("sdlc", Json("""{"model":"waterfall","currentPhase":2,"completed":true,"notes":{}}""")), "Premature roadmap completion rejected");
Check(!ToolDocumentValidation.Valid("sdlc", Json("""{"model":"waterfall","currentPhase":{},"completed":false,"notes":{}}""")), "Malformed numeric property rejected without throwing");
await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
{
    await using var transaction = await db.Database.BeginTransactionAsync();
    try
    {
        var project = await db.Projects.AsNoTracking().FirstAsync();
        var owner = await db.Organizations.Where(o => o.Id == project.OrganizationId).Select(o => o.OwnerUserId).SingleAsync();
        foreach (var (tool, document) in documents)
        {
            var initial = Require(await service.Get(project.OrganizationId, project.Id, owner, tool, default));
            var saved = Require(await service.Save(project.OrganizationId, project.Id, owner, tool, new(initial.Revision, document), default));
            var loaded = Require(await service.Get(project.OrganizationId, project.Id, owner, tool, default));
            Check(loaded.Revision == initial.Revision + 1 && loaded.Document?.GetRawText() == document.GetRawText(), $"{tool} round trips through SQL with its own revision");
            Check((await service.Save(project.OrganizationId, project.Id, owner, tool, new(initial.Revision, document), default)).Error?.Type == ErrorType.Conflict, $"{tool} stale writes cannot overwrite newer changes");
            Check((await service.Save(project.OrganizationId, project.Id, Guid.NewGuid(), tool, new(saved.Revision, document), default)).Error?.Type == ErrorType.NotFound, $"{tool} non-members cannot write");
            Check((await service.Get(Guid.NewGuid(), project.Id, owner, tool, default)).Error?.Type == ErrorType.NotFound, $"{tool} cannot be read through another organization");
            Check((await service.Save(project.OrganizationId, project.Id, owner, tool, new(saved.Revision, Json("{}")), default)).Error?.Type == ErrorType.Validation, $"{tool} malformed documents rejected");
        }
        Check((await service.Get(project.OrganizationId, project.Id, owner, "unknown", default)).Error?.Type == ErrorType.NotFound, "Unknown project tools rejected");
        var erd = Require(await service.Get(project.OrganizationId, project.Id, owner, "erd", default));
        var arrowSaved = Require(await service.Save(project.OrganizationId, project.Id, owner, "erd", new(erd.Revision, reversed), default));
        Check(Require(await service.Get(project.OrganizationId, project.Id, owner, "erd", default)).Document?.GetRawText() == reversed.GetRawText(), "Custom arrows and many-to-one relationships persist through SQL");
    }
    finally {await transaction.RollbackAsync(); Console.WriteLine("All project document test writes rolled back.");}
});
static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new Exception(result.Error!.Code);
static void Check(bool condition, string message) {if (!condition) throw new Exception(message); Console.WriteLine("PASS: " + message);}
