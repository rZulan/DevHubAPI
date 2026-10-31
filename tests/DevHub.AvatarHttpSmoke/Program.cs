using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using DevHub.Api.Controllers;
using DevHub.Api.Realtime;
using DevHub.Application;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Organizations;
using DevHub.Application.Teams;
using DevHub.Domain.Users;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Exercise MVC record validation and persistence over HTTP in a disposable local database.
var configuration = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables().Build();
await using var isolated = new IsolatedSmokeDatabase(configuration);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName = "Development"});
builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
builder.Services.AddApplication().AddInfrastructure(configuration);
builder.Services.AddControllers().AddApplicationPart(typeof(OrganizationsController).Assembly);
builder.Services.AddSignalR(); builder.Services.AddSingleton<WorkshopSubscriptions>();
builder.Services.AddSingleton<WorkshopPresenceTracker>(); builder.Services.AddScoped<WorkshopRealtime>();
await using var app = builder.Build();
app.UseRouting(); app.UseAuthentication();
app.Use(async (context, next) =>
{
    // The test identity header is only installed in this isolated test host.
    if (Guid.TryParse(context.Request.Headers["X-Test-User"], out var id)) context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, id.ToString())], "Smoke"));
    await next();
});
app.UseAuthorization(); app.MapControllers();
Guid organizationId, teamId, owner, leader;
await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); await isolated.Seed(db);
    var organization = (await scope.ServiceProvider.GetRequiredService<IOrganizationRepository>().GetByIdAsync((await db.Organizations.SingleAsync()).Id))!;
    organizationId = organization.Id; owner = organization.OwnerUserId;
    var member = User.Create("leader@example.test", "avatar_leader", "Avatar", "Leader", "test-only", DateTimeOffset.UtcNow); leader = member.Id;
    db.Users.Add(member); organization.AddMember(leader, DateTimeOffset.UtcNow);
    var team = organization.Teams.Single(); teamId = team.Id;
    team.Update(team.Name, team.Description, leader, DateTimeOffset.UtcNow); await db.SaveChangesAsync();
}
await app.StartAsync();
using var client = new HttpClient {BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
var organizationPath = $"/api/organizations/{organizationId}/avatar";
var teamPath = $"/api/organizations/{organizationId}/teams/{teamId}/avatar";
const string png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j1ioAAAAASUVORK5CYII=";
async Task<HttpResponseMessage> Save(string path, string? avatar, Guid? user)
{
    using var request = new HttpRequestMessage(HttpMethod.Put, path) {Content = JsonContent.Create(new {avatarUrl = avatar})};
    if (user is not null) request.Headers.Add("X-Test-User", user.ToString());
    return await client.SendAsync(request);
}
void Check(bool condition, string label) {if (!condition) throw new Exception(label); Console.WriteLine("PASS avatar HTTP: " + label);}
async Task<T> Read<T>(HttpResponseMessage response)
{
    if (response.StatusCode != HttpStatusCode.OK) throw new Exception($"Expected 200, received {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    return (await response.Content.ReadFromJsonAsync<T>())!;
}
var saved = await Read<OrganizationResponse>(await Save(organizationPath, png, owner));
Check(saved.AvatarUrl == png, "organization icon saves through MVC request binding");
await using (var scope = app.Services.CreateAsyncScope())
    Check((await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Organizations.AsNoTracking().SingleAsync()).AvatarUrl == png, "saved organization icon is persisted in SQL");
Check((await Read<OrganizationResponse>(await Save(organizationPath, png, owner))).AvatarUrl == png, "saving the same icon twice succeeds");
Check((await Save(organizationPath, png, null)).StatusCode == HttpStatusCode.Unauthorized, "anonymous icon changes are rejected");
Check((await Save(organizationPath, png, leader)).StatusCode == HttpStatusCode.Forbidden, "team leaders do not gain organization icon permissions");
foreach (var invalid in new[] {"not-an-image", "data:image/png;base64,invalid", "data:image/svg+xml;base64,PHN2Zy8+"})
{
    Check((await Save(organizationPath, invalid, owner)).StatusCode == HttpStatusCode.BadRequest, "invalid organization images return validation errors instead of 500");
    Check((await Save(teamPath, invalid, leader)).StatusCode == HttpStatusCode.BadRequest, "invalid team images return validation errors instead of 500");
}
var teamSaved = await Read<TeamResponse>(await Save(teamPath, png, leader));
Check(teamSaved.AvatarUrl == png, "team leader can save its icon through MVC request binding");
Check((await Read<OrganizationResponse>(await Save(organizationPath, null, owner))).AvatarUrl is null, "organization icon removal works");
Check((await Read<TeamResponse>(await Save(teamPath, null, leader))).AvatarUrl is null, "team icon removal works");
await app.StopAsync();
Console.WriteLine("Avatar HTTP and SQL checks complete; disposable database will be removed.");
