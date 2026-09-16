using System.Reflection;
using DevHub.Application.Common;
using DevHub.Application.Todos;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Run from api/: dotnet run --project tests/DevHub.TodoSmoke -c Release
// All test writes are rolled back, including on assertion failure.
var configuration = new ConfigurationBuilder()
    .SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json")
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true)
    .AddEnvironmentVariables().Build();
var services = new ServiceCollection();
services.AddLogging();
services.AddInfrastructure(configuration);
await using var provider = services.BuildServiceProvider();
await using var scope = provider.CreateAsyncScope();
var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
var service = scope.ServiceProvider.GetRequiredService<ITodoService>();
await using var transaction = await db.Database.BeginTransactionAsync();
try
{
    var project = await db.Projects.AsNoTracking().FirstAsync();
    var owner = await db.Organizations.Where(o => o.Id == project.OrganizationId).Select(o => o.OwnerUserId).SingleAsync();
    var org = project.OrganizationId;
    var ct = CancellationToken.None;
    var target = new DateOnly(2026, 12, 31);
    var first = Require(await service.Create(org, project.Id, owner, new("Persistence smoke A", "Context", target), ct));
    var second = Require(await service.Create(org, project.Id, owner, new("Persistence smoke B", null, null), ct));
    var task = Require(await service.AddTask(org, project.Id, first.Id, owner, new("Saved task", "Task context", "pending"), ct));
    Require(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("progress"), ct));
    db.ChangeTracker.Clear();
    var reloaded = Require(await service.List(org, project.Id, owner, ct));
    Check(reloaded.Single(t => t.Id == first.Id).TargetDate == target, "Target date persists");
    Check(reloaded.Single(t => t.Id == first.Id).Tasks.Single().Status == "progress", "Task move persists after reload");
    Check(reloaded.Single(t => t.Id == second.Id).Tasks.Count == 0, "TODO boards are isolated");
    Check(reloaded.Single(t => t.Id == second.Id).TargetDate is null, "Target date is optional");
    Check(Require(await service.Get(org, project.Id, first.Id, owner, ct)).Tasks.Single().Status == "progress", "Direct board lookup includes tasks");
    Check(!(await service.Get(org, Guid.NewGuid(), first.Id, owner, ct)).IsSuccess, "Cross-project board lookup rejected");
    for (var index = 0; index < 13; index++)
        Require(await service.Create(org, project.Id, owner, new($"Pagination smoke {index}", null, null), ct));
    var firstPage = Require(await service.ListPage(org, project.Id, owner, 0, ct));
    var secondPage = Require(await service.ListPage(org, project.Id, owner, 1, ct));
    Check(firstPage.Items.Count == 12 && firstPage.HasMore, "First page is bounded and indicates more TODOs");
    Check(secondPage.Items.Count > 0 && !firstPage.Items.Select(t => t.Id).Intersect(secondPage.Items.Select(t => t.Id)).Any(), "Next page contains distinct TODOs");
    var total = Require(await service.List(org, project.Id, owner, ct)).Count;
    var lastPage = Require(await service.ListPage(org, project.Id, owner, (total - 1) / 12, ct));
    Check(!lastPage.HasMore && lastPage.Items.Count == (total - 1) % 12 + 1, "Final page stops pagination");
    Check(!(await service.ListPage(org, project.Id, owner, -1, ct)).IsSuccess, "Negative page rejected");
    foreach (var size in new[] { 4, 8, 12 })
    {
        var responsiveFirst = Require(await service.ListPage(org, project.Id, owner, 0, ct, size));
        var responsiveNext = Require(await service.ListPage(org, project.Id, owner, 1, ct, size));
        Check(responsiveFirst.Items.Count == size && responsiveFirst.HasMore, $"Four rows at {size / 4} columns");
        Check(!responsiveFirst.Items.Select(t => t.Id).Intersect(responsiveNext.Items.Select(t => t.Id)).Any(), $"Distinct next page at size {size}");
    }
    Check(!(await service.ListPage(org, project.Id, owner, 0, ct, 0)).IsSuccess, "Zero page size rejected");
    Check(!(await service.ListPage(org, project.Id, owner, 0, ct, 101)).IsSuccess, "Excessive page size rejected");
    Check(!(await service.ListPage(org, project.Id, Guid.NewGuid(), 0, ct)).IsSuccess, "Non-member pagination rejected");
    Require(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("done"), ct));
    Check(!(await service.MoveTask(org, project.Id, second.Id, task.Id, owner, new("pending"), ct)).IsSuccess, "Cross-board task move rejected");
    Check(!(await service.List(org, project.Id, Guid.NewGuid(), ct)).IsSuccess, "Non-member access rejected");
    Check(!(await service.List(Guid.NewGuid(), project.Id, owner, ct)).IsSuccess, "Cross-organization access rejected");
    Check(!(await service.AddTask(org, Guid.NewGuid(), first.Id, owner, new("Invalid scope", null, "pending"), ct)).IsSuccess, "Cross-project write rejected");
    Check(!(await service.Create(org, project.Id, owner, new("   ", null, null), ct)).IsSuccess, "Whitespace TODO name rejected");
    Check(!(await service.AddTask(org, project.Id, first.Id, owner, new("   ", null, "pending"), ct)).IsSuccess, "Whitespace task title rejected");
    Check(!(await service.MoveTask(org, project.Id, first.Id, task.Id, owner, new("invalid"), ct)).IsSuccess, "Invalid task status rejected");
    Console.WriteLine("TODO persistence smoke checks passed.");
}
finally
{
    await transaction.RollbackAsync();
    Console.WriteLine("Test writes rolled back.");
}

static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new Exception(result.Error!.Code);
static void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine($"PASS: {name}");
}
