using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DevHub.Api.BugReports;
using DevHub.Api.Controllers;
using DevHub.Application;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.BugReports;
using DevHub.Domain.Organizations;
using DevHub.Domain.Projects;
using DevHub.Domain.Users;
using DevHub.Infrastructure;
using DevHub.Infrastructure.BugReports;
using DevHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Real MVC/multipart/authorization/rate-limiter and SQL checks in a disposable local database.
// Run from api/: dotnet run --project tests/DevHub.BugReportsSmoke -c Release
var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
await using var isolated = new IsolatedSmokeDatabase(configuration);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName = "Development"});
builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
builder.Services.AddApplication().AddInfrastructure(configuration);
builder.Services.AddControllers().AddApplicationPart(typeof(BugReportsController).Assembly);
builder.Services.AddBugReportRateLimiting();
await using var app = builder.Build();
app.UseRouting(); app.UseAuthentication();
// Test-only identities and IPs never enter the production app.
app.Use(async (context, next) =>
{
    if (Guid.TryParse(context.Request.Headers["X-Test-User"], out var userId)) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, userId.ToString())], "Smoke"));
    if (IPAddress.TryParse(context.Request.Headers["X-Test-IP"], out var address)) context.Connection.RemoteIpAddress = address;
    await next();
});
app.UseRateLimiter(); app.UseAuthorization(); app.MapControllers();
Guid owner, orgId, projectId, viewer;
await using (var seed = app.Services.CreateAsyncScope())
{
    var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await isolated.Seed(db);
    var project = await db.Projects.SingleAsync(); projectId = project.Id; orgId = project.OrganizationId;
    var repository = seed.ServiceProvider.GetRequiredService<IOrganizationRepository>(); var organization = (await repository.GetByIdAsync(orgId))!; owner = organization.OwnerUserId;
    var member = User.Create("viewer@example.test", "bug_viewer", "Bug", "Viewer", "test-only-hash", DateTimeOffset.UtcNow); viewer = member.Id;
    db.Users.Add(member); organization.AddMember(viewer, DateTimeOffset.UtcNow); organization.Teams.Single().AddMember(viewer, DateTimeOffset.UtcNow); await db.SaveChangesAsync();
}
await app.StartAsync();
using var client = new HttpClient {BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
var path = $"/api/organizations/{orgId}/projects/{projectId}/bug-reports"; var ip = 1;
async Task<HttpResponseMessage> Send(HttpMethod method, string url, object? json = null, Guid? user = null, HttpContent? body = null, string? address = null)
{
    using var request = new HttpRequestMessage(method, url) {Content = body ?? (json is null ? null : JsonContent.Create(json))};
    if (user is not null) request.Headers.Add("X-Test-User", user.ToString());
    request.Headers.Add("X-Test-IP", address ?? $"127.0.1.{ip++}");
    return await client.SendAsync(request);
}
async Task<T> Read<T>(HttpResponseMessage response, int expected = 200)
{
    if ((int)response.StatusCode != expected) throw new Exception($"Expected {expected}, received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    return (await response.Content.ReadFromJsonAsync<T>())!;
}
void Check(bool condition, string label) {if (!condition) throw new Exception(label); Console.WriteLine("PASS: " + label);}
var portal = await Read<BugPortalResponse>(await Send(HttpMethod.Get, path + "/portal", user: owner));
Check(!portal.Enabled && portal.Token == "", "Public reporting starts disabled without a link");
portal = await Read<BugPortalResponse>(await Send(HttpMethod.Put, path + "/portal", new {enabled = true, rotate = false, revision = portal.Revision}, owner));
var publicPath = $"/api/public/bug-reports/{portal.Token}";
Check(portal.Enabled && portal.Token.Length == 64, "Enabling creates an unguessable public token");
Check(portal.ShortCode?.Length == 14 && BugPortalLink.LookupToken(portal.ShortCode) == portal.Token[..20], "Portal settings expose a compact URL-safe code for existing tokens");
Check(BugPortalLink.ShortCode("00112233445566778899" + new string('a', 44)) == "ABEiM0RVZneImQ", "Short links have a stable URL-safe format");
var shortPublicPath = $"/api/public/bug-reports/{portal.ShortCode}";
var category = await Read<BugCategoryResponse>(await Send(HttpMethod.Post, path + "/categories", new {name = "Interface", archived = false}, owner));
Check((await Send(HttpMethod.Post, path + "/categories", new {name = " interface ", archived = false}, owner)).StatusCode == HttpStatusCode.BadRequest, "Duplicate categories rejected without a server error");
var publicResponse = await Send(HttpMethod.Get, publicPath); var publicJson = await Read<JsonElement>(publicResponse);
Check(publicJson.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(new[] {"categories", "projectName"}), "Public form exposes only project name and available categories");
Check(publicJson.GetProperty("categories")[0].GetProperty("id").GetGuid() == category.Id, "Project categories appear on the anonymous form");
var shortForm = await Read<JsonElement>(await Send(HttpMethod.Get, shortPublicPath));
Check(shortForm.GetRawText() == publicJson.GetRawText(), "Short and full links load the same public form without private data");
foreach (var invalid in new[] {"short", "abcdefghijklmn!", "ABEiM0RVZneImR", "AAAAAAAAAAAAAAAAAAAA"})
    Check((await Send(HttpMethod.Get, $"/api/public/bug-reports/{invalid}")).StatusCode == HttpStatusCode.NotFound, "Malformed and noncanonical short links are unavailable");
var image = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jR1kAAAAASUVORK5CYII=");
MultipartFormDataContent Form(Guid? submissionId = null, Guid? categoryId = null, string email = "tester@example.test", string fileName = "screenshot.png", byte[]? bytes = null, string? steps = "1. Open settings\n2. Click Save", string? environment = "Chrome on Windows")
{
    var form = new MultipartFormDataContent();
    foreach (var (name, value) in new Dictionary<string, string?> {{"submissionId", (submissionId ?? Guid.NewGuid()).ToString()}, {"email", email}, {"title", "Save button does not respond"}, {"description", "Clicking Save leaves the settings unchanged."}, {"steps", steps}, {"environment", environment}, {"priority", "high"}, {"categoryId", (categoryId ?? category.Id).ToString()}}) if (value is not null) form.Add(new StringContent(value), name);
    form.Add(new ByteArrayContent(bytes ?? image), "files", fileName); return form;
}
var submissionId = Guid.NewGuid();
var receipt = await Read<BugReceipt>(await Send(HttpMethod.Post, shortPublicPath, body: Form(submissionId)), 201);
Check(receipt.Reference.StartsWith("BUG-"), "Anonymous multipart report accepted using email without a DevHub account");
var retry = await Read<BugReceipt>(await Send(HttpMethod.Post, publicPath, body: Form(submissionId)), 201);
Check(retry.Reference == receipt.Reference, "Retrying across short and full links does not create a duplicate report");
Check((await Send(HttpMethod.Post, publicPath, body: Form(email: "bad email"))).StatusCode == HttpStatusCode.BadRequest, "Malformed email rejected");
Check((await Send(HttpMethod.Post, publicPath, body: Form(fileName: "fake.png", bytes: Encoding.UTF8.GetBytes("not an image")))).StatusCode == HttpStatusCode.BadRequest, "Spoofed image content rejected");
Check((await Send(HttpMethod.Post, publicPath, body: Form(fileName: "program.exe"))).StatusCode == HttpStatusCode.BadRequest, "Unsupported executable attachment rejected");
Check((await Send(HttpMethod.Post, publicPath, body: Form(categoryId: Guid.NewGuid()))).StatusCode == HttpStatusCode.BadRequest, "Foreign or missing categories rejected");
Check((await Send(HttpMethod.Post, path, user: owner, body: Form(fileName: "oversized.log", bytes: new byte[BugFileValidation.MaxFileBytes + 1]))).StatusCode == HttpStatusCode.BadRequest, "Attachment size limit enforced over HTTP");
var inbox = await Read<BugInboxResponse>(await Send(HttpMethod.Get, path, user: owner));
Check(inbox.Total == 1 && inbox.NewCount == 1 && inbox.Items[0].Email == "tester@example.test" && inbox.Items[0].AttachmentCount == 1, "Private inbox retains sender reference, category and attachment metadata in SQL");
var report = await Read<BugDetail>(await Send(HttpMethod.Get, path + $"/{inbox.Items[0].Id}", user: owner));
Check((await Send(HttpMethod.Get, path)).StatusCode == HttpStatusCode.Unauthorized, "Anonymous visitors cannot list private reports");
Check((await Send(HttpMethod.Get, path + $"/{report.Id}/attachments/{report.Attachments[0].Id}")).StatusCode == HttpStatusCode.Unauthorized, "Anonymous visitors cannot download attachments");
Check((await Send(HttpMethod.Get, publicPath + $"/{report.Id}")).StatusCode == HttpStatusCode.NotFound, "Public token cannot read an existing report");
Check((await Send(HttpMethod.Get, path.Replace(projectId.ToString(), Guid.NewGuid().ToString()) + $"/{report.Id}", user: owner)).StatusCode == HttpStatusCode.NotFound, "Reports cannot be accessed through another project");
Check((await Send(HttpMethod.Get, path.Replace(orgId.ToString(), Guid.NewGuid().ToString()) + $"/{report.Id}", user: owner)).StatusCode == HttpStatusCode.NotFound, "Reports cannot be accessed through another organization");
var downloaded = await Send(HttpMethod.Get, path + $"/{report.Id}/attachments/{report.Attachments[0].Id}", user: owner);
Check(downloaded.IsSuccessStatusCode && (await downloaded.Content.ReadAsByteArrayAsync()).SequenceEqual(image) && downloaded.Content.Headers.ContentDisposition?.DispositionType == "attachment" && downloaded.Headers.GetValues("X-Content-Type-Options").Single() == "nosniff", "Private attachment bytes round-trip safely as a download");
Check((await Send(HttpMethod.Get, path + "/portal", user: viewer)).StatusCode == HttpStatusCode.Forbidden, "Project viewers cannot change or retrieve link controls");
var triage = new TriageBugRequest(report.Revision, "in-progress", "critical", category.Id, "Investigating the form handler.");
Check((await Send(HttpMethod.Put, path + $"/{report.Id}", triage, viewer)).StatusCode == HttpStatusCode.Forbidden, "Project viewers cannot triage without permission");
await using (var grant = app.Services.CreateAsyncScope())
{
    var db = grant.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var organization = (await grant.ServiceProvider.GetRequiredService<IOrganizationRepository>().GetByIdAsync(orgId))!;
    var role = organization.AddRole("Bug triage", "#38bdf8", [OrganizationPermissions.ManageBugReports], DateTimeOffset.UtcNow);
    organization.AssignRole(viewer, role.Id, DateTimeOffset.UtcNow); await db.SaveChangesAsync();
}
Check((await Send(HttpMethod.Get, path + "/portal", user: viewer)).IsSuccessStatusCode, "Manage bug reports can be granted without Administrator");
var updated = await Read<BugDetail>(await Send(HttpMethod.Put, path + $"/{report.Id}", triage, owner));
Check(updated.Status == "in-progress" && updated.Revision == 1 && updated.InternalNotes == triage.InternalNotes, "Triage status, priority and private notes persist");
Check((await Send(HttpMethod.Put, path + $"/{report.Id}", triage, owner)).StatusCode == HttpStatusCode.Conflict, "Stale triage changes cannot overwrite another session");
Check((await Read<BugInboxResponse>(await Send(HttpMethod.Get, path + $"?status=in-progress&categoryId={category.Id}&search=Save", user: viewer))).Total == 1, "Category, status, and search filters work through SQL");
var independentGrant = await Read<BugDetail>(await Send(HttpMethod.Put, path + $"/{report.Id}", triage with {Revision = updated.Revision, Status = "triaged"}, viewer));
Check(independentGrant.Status == "triaged", "The dedicated bug permission allows triage changes");
category = await Read<BugCategoryResponse>(await Send(HttpMethod.Put, path + $"/categories/{category.Id}", new {name = "User interface", archived = true}, owner));
var archivedForm = await Read<PublicBugFormResponse>(await Send(HttpMethod.Get, publicPath));
Check(archivedForm.Categories.Count == 0, "Archived categories hidden from public submissions");
Check((await Send(HttpMethod.Post, publicPath, body: Form())).StatusCode == HttpStatusCode.BadRequest, "An open form cannot submit an archived category");
Check((await Read<BugDetail>(await Send(HttpMethod.Get, path + $"/{report.Id}", user: owner))).CategoryId == category.Id, "Archiving a category preserves existing report classification");
category = await Read<BugCategoryResponse>(await Send(HttpMethod.Put, path + $"/categories/{category.Id}", new {name = "User interface", archived = false}, owner));
Check((await Read<PublicBugFormResponse>(await Send(HttpMethod.Get, publicPath))).Categories.Single().Name == "User interface", "Renamed categories can be restored without changing their identity");
portal = await Read<BugPortalResponse>(await Send(HttpMethod.Put, path + "/portal", new {enabled = false, rotate = false, revision = portal.Revision}, owner));
Check((await Send(HttpMethod.Get, publicPath)).StatusCode == HttpStatusCode.NotFound && (await Send(HttpMethod.Post, publicPath, body: Form())).StatusCode == HttpStatusCode.NotFound, "Disabling a link blocks both form loading and submissions");
Check((await Send(HttpMethod.Get, shortPublicPath)).StatusCode == HttpStatusCode.NotFound && (await Send(HttpMethod.Post, shortPublicPath, body: Form())).StatusCode == HttpStatusCode.NotFound, "Disabling a portal also blocks short-link reads and submissions");
Check((await Send(HttpMethod.Get, path + $"/{report.Id}", user: owner)).IsSuccessStatusCode, "Disabling public access preserves the private inbox");
Check((await Send(HttpMethod.Post, path, user: owner, body: Form(fileName: "browser.log", bytes: Encoding.UTF8.GetBytes("Error: button handler failed")))).StatusCode == HttpStatusCode.Created, "Team reports with log attachments work while the public link is disabled");
portal = await Read<BugPortalResponse>(await Send(HttpMethod.Put, path + "/portal", new {enabled = true, rotate = true, revision = portal.Revision}, owner));
Check((await Send(HttpMethod.Get, publicPath)).StatusCode == HttpStatusCode.NotFound && (await Send(HttpMethod.Get, $"/api/public/bug-reports/{portal.Token}")).IsSuccessStatusCode, "Replacing a public link invalidates the old URL");
var currentShortPath = $"/api/public/bug-reports/{portal.ShortCode}";
Check((await Send(HttpMethod.Get, shortPublicPath)).StatusCode == HttpStatusCode.NotFound && (await Send(HttpMethod.Post, shortPublicPath, body: Form())).StatusCode == HttpStatusCode.NotFound && (await Send(HttpMethod.Get, currentShortPath)).IsSuccessStatusCode, "Replacing the public link retires its short version too");
for (var i = 0; i < 10; i++) Check((await Send(HttpMethod.Post, i % 2 == 0 ? currentShortPath : $"/api/public/bug-reports/{portal.Token}", body: Form(), address: "127.0.2.250")).StatusCode == HttpStatusCode.Created, $"Rate limiter permits request {i + 1}");
var limited = await Send(HttpMethod.Post, currentShortPath, body: Form(), address: "127.0.2.250");
Check(limited.StatusCode == HttpStatusCode.TooManyRequests && limited.Headers.RetryAfter is not null, "Public submission throttling returns 429 with a retry interval");
Check(BugFileValidation.SafeName("..\\secret/screenshot.png") == "screenshot.png" && BugFileValidation.ContentType("binary.txt", [0, 1, 2]) is null, "Attachment names are sanitized and binary content cannot masquerade as text");
await using (var collision = app.Services.CreateAsyncScope())
{
    var db = collision.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var project = await db.Projects.SingleAsync(p => p.Id == projectId);
    var other = Project.Create(orgId, project.TeamId, owner, "Prefix collision", "Test-only", ProjectStatus.Planning, [], null, null, null, null, null, null, [], null, null, DateTimeOffset.UtcNow);
    var colliding = BugReportPortal.Create(other.Id, portal.Token[..63] + (portal.Token[^1] == 'a' ? "b" : "a")); colliding.Update(false);
    db.Projects.Add(other); db.BugReportPortals.Add(colliding); await db.SaveChangesAsync();
}
Check((await Send(HttpMethod.Get, currentShortPath)).StatusCode == HttpStatusCode.NotFound && (await Send(HttpMethod.Post, currentShortPath, body: Form())).StatusCode == HttpStatusCode.NotFound, "Ambiguous prefixes never open or submit to another project, including disabled portals");
Check((await Send(HttpMethod.Get, $"/api/public/bug-reports/{portal.Token}")).IsSuccessStatusCode, "The full link still works if a short prefix is ambiguous");
foreach (var optional in new string?[] {"", null})
{
    var submitted = await Read<BugReceipt>(await Send(HttpMethod.Post, $"/api/public/bug-reports/{portal.Token}", body: Form(steps: optional, environment: optional)), 201);
    Check(!string.IsNullOrEmpty(submitted.Reference), optional is null ? "Public multipart submission accepts omitted optional steps and environment" : "Public multipart submission accepts blank optional steps and environment with screenshot");
    var teamSubmitted = await Read<BugReceipt>(await Send(HttpMethod.Post, path, user: owner, body: Form(steps: optional, environment: optional)), 201);
    Check(!string.IsNullOrEmpty(teamSubmitted.Reference), "Team multipart submission accepts empty or omitted optional fields");
}
await app.StopAsync();
Console.WriteLine("Bug report HTTP and SQL checks completed; disposable database will be removed.");
