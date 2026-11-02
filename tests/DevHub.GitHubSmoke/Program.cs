using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using DevHub.Api.Controllers;
using DevHub.Api.Realtime;
using DevHub.Api.Security;
using DevHub.Application;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.GitHub;
using DevHub.Application.Projects;
using DevHub.Domain.Authentication;
using DevHub.Domain.Users;
using DevHub.Infrastructure;
using DevHub.Infrastructure.GitHub;
using DevHub.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("src/DevHub.Api"))
    .AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", optional: true)
    .AddUserSecrets(Assembly.Load("DevHub.Api"), optional: true).AddEnvironmentVariables()
    .AddInMemoryCollection(new Dictionary<string,string?> {
        ["Integrations:GitHub:ClientId"] = "test-client", ["Integrations:GitHub:ClientSecret"] = "test-secret",
        ["Integrations:GitHub:AppId"] = "42", ["Integrations:GitHub:AppSlug"] = "test-app",
        ["Integrations:GitHub:CallbackUrl"] = "https://localhost:7116/api/integrations/github/callback"
    }).Build();
await using var isolated = new IsolatedSmokeDatabase(config);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions {EnvironmentName = "Development"});
builder.Configuration.AddConfiguration(config);
builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
builder.Services.AddApplication().AddInfrastructure(config);
var fake = new FakeGitHub();
builder.Services.AddHttpClient<GitHubIntegration>().ConfigurePrimaryHttpMessageHandler(() => fake);
builder.Services.AddControllers().AddApplicationPart(typeof(GitHubIntegrationController).Assembly);
builder.Services.AddRequestSecurity(config);
builder.Services.AddSignalR(); builder.Services.AddSingleton<WorkshopSubscriptions>();
builder.Services.AddSingleton<WorkshopPresenceTracker>(); builder.Services.AddScoped<WorkshopRealtime>();
await using var app = builder.Build();
app.UseRouting(); app.UseAuthentication();
app.Use(async (context, next) => {
    if (Guid.TryParse(context.Request.Headers["X-Test-User"], out var id))
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, id.ToString()), new Claim(ClaimTypes.NameIdentifier, id.ToString())], "Smoke"));
    await next();
});
app.UseMiddleware<BrowserRequestSecurityMiddleware>(); app.UseAuthorization(); app.MapControllers();
// This endpoint and header identity exist only in this disposable test host.
app.MapGet("/smoke/session/{id:guid}", async (HttpContext context, Guid id) => {
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString()), new Claim("sub", id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme));
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
});
Guid user, orgId, teamId, projectId, outsider;
await using (var scope = app.Services.CreateAsyncScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await isolated.Seed(db, db.Database.GetMigrations().Last());
    var org = await db.Organizations.SingleAsync(); orgId = org.Id; user = org.OwnerUserId;
    var project = await db.Projects.SingleAsync(); projectId = project.Id; teamId = project.TeamId;
    var other = User.Create("outside@example.test", "github_outside", "Other", "User", "test-hash", DateTimeOffset.UtcNow);
    db.Add(other); outsider = other.Id; await db.SaveChangesAsync();
}
await app.StartAsync();
using var client = new HttpClient(new HttpClientHandler {AllowAutoRedirect = false, UseCookies = false}) {BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
async Task<HttpResponseMessage> Send(HttpMethod method, string path, Guid? identity = null, object? body = null, string? cookie = null, string? origin = null) {
    using var request = new HttpRequestMessage(method, path);
    if (identity is not null) request.Headers.Add("X-Test-User", identity.ToString());
    if (cookie is not null) request.Headers.Add("Cookie", cookie);
    if (origin is not null) request.Headers.Add("Origin", origin);
    if (body is not null) request.Content = JsonContent.Create(body);
    return await client.SendAsync(request);
}
async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK) {
    if (response.StatusCode != expected) throw new Exception($"Expected {expected}; received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    return JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
}
Check((await Send(HttpMethod.Post, "/api/integrations/github/authorize")).StatusCode == HttpStatusCode.Unauthorized, "anonymous authorization is rejected");
Check((await Send(HttpMethod.Post, "/api/integrations/github/authorize", user, origin: "https://evil.example")).StatusCode == HttpStatusCode.Forbidden, "cross-origin authorization is rejected");
var sessionResponse = await Send(HttpMethod.Get, $"/smoke/session/{user}");
var session = sessionResponse.Headers.GetValues("Set-Cookie").First().Split(';')[0];
var start = await Send(HttpMethod.Post, "/api/integrations/github/authorize", user);
var startData = await Read(start);
var authorizeUrl = new Uri(startData.GetProperty("url").GetString()!);
var query = QueryHelpers.ParseQuery(authorizeUrl.Query);
Check(authorizeUrl.Host == "github.com" && query["code_challenge_method"] == "S256" && query["scope"] == "offline_access", "authorization uses GitHub App client and PKCE");
var flowCookie = start.Headers.GetValues("Set-Cookie").Single().Split(';')[0];
Check(start.Headers.GetValues("Set-Cookie").Single().Contains("httponly", StringComparison.OrdinalIgnoreCase), "correlation state is HttpOnly");
var callback = $"/api/integrations/github/callback?code=valid-code&state={query["state"]}";
var wrong = await Send(HttpMethod.Get, callback.Replace(query["state"].ToString(), "forged-state"), cookie: session + "; " + flowCookie);
Check(wrong.Headers.Location!.Query.Contains("failed") && fake.CodeExchanges == 0, "forged state cannot exchange an authorization code");
var wrongUserResponse = await Send(HttpMethod.Get, $"/smoke/session/{outsider}");
var wrongSession = wrongUserResponse.Headers.GetValues("Set-Cookie").First().Split(';')[0];
var mismatched = await Send(HttpMethod.Get, callback, cookie: wrongSession + "; " + flowCookie);
Check(mismatched.Headers.Location!.Query.Contains("failed") && fake.CodeExchanges == 0, "callback is bound to the initiating DevHub session");
var success = await Send(HttpMethod.Get, callback, cookie: session + "; " + flowCookie);
Check(success.Headers.Location!.Query.Contains("connected") && fake.CodeExchanges == 1 && fake.UsedVerifier, "valid callback stores connection using PKCE verifier");
await using (var scope = app.Services.CreateAsyncScope()) {
    var stored = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<GitHubConnection>().SingleAsync();
    Check(stored.UserId == user && stored.ProtectedAccessToken != "fake-access" && stored.ProtectedRefreshToken != "fake-refresh", "GitHub access and refresh tokens are protected in SQL");
}
var status = await Read(await Send(HttpMethod.Get, "/api/integrations/github/status", user));
Check(status.GetProperty("connected").GetBoolean() && !status.ToString().Contains("fake-access"), "status never returns credentials");
Check(!(await Read(await Send(HttpMethod.Get, "/api/integrations/github/status", outsider))).GetProperty("connected").GetBoolean(), "connections are isolated by DevHub account");
var installations = await Read(await Send(HttpMethod.Get, "/api/integrations/github/installations", user));
Check(installations.GetArrayLength() == 1 && installations[0].GetProperty("id").GetInt64() == 100, "repository discovery filters installations to this GitHub App");
var repositories = await Read(await Send(HttpMethod.Get, "/api/integrations/github/installations/100/repositories", user));
Check(repositories.GetProperty("repositories").GetArrayLength() == 2, "authorized public and private repositories are listed");
Check((await Send(HttpMethod.Get, "/api/integrations/github/installations/999/repositories", user)).StatusCode == HttpStatusCode.Forbidden, "arbitrary installation IDs cannot expose repositories");
Check((await Send(HttpMethod.Get, "/api/integrations/github/installations/100/repositories", outsider)).StatusCode == HttpStatusCode.Conflict, "other DevHub accounts cannot borrow the connector's credential");
await using (var scope = app.Services.CreateAsyncScope()) {
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var connection = await db.Set<GitHubConnection>().SingleAsync(); connection.ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-1); await db.SaveChangesAsync();
}
await Read(await Send(HttpMethod.Get, "/api/integrations/github/installations", user));
Check(fake.Refreshes == 1, "expired user access token is refreshed server-side");
object Payload(object[] selected) => new {name = "Connected project", summary = "Two connected repositories", status = "Planning", teamId, leadUserId = user,
    techStack = Array.Empty<string>(), environments = Array.Empty<string>(), gitHubRepositories = selected};
var selections = new object[] {new {installationId = 100, repositoryId = 10, label = "Frontend"}, new {installationId = 100, repositoryId = 20, label = "Backend"}};
var path = $"/api/organizations/{orgId}/projects";
var created = await Read(await Send(HttpMethod.Post, path, user, Payload(selections)), HttpStatusCode.Created);
var newProjectId = created.GetProperty("id").GetGuid();
Check(created.GetProperty("gitHubRepositories").GetArrayLength() == 2, "project creation binds and persists multiple labeled repository links");
var reloaded = await Read(await Send(HttpMethod.Get, $"{path}/{newProjectId}", user));
Check(reloaded.GetProperty("gitHubRepositories")[1].GetProperty("label").GetString() == "Backend", "repository labels survive reload");
Check((await Send(HttpMethod.Put, $"{path}/{newProjectId}", outsider, Payload(selections))).StatusCode == HttpStatusCode.NotFound, "nonmembers cannot connect repositories to a project");
var forged = new object[] {new {installationId = 100, repositoryId = 999, label = "Forged"}};
Check((await Send(HttpMethod.Put, $"{path}/{newProjectId}", user, Payload(forged))).StatusCode == HttpStatusCode.BadRequest, "unavailable repository IDs are rejected on project save");
var duplicates = new object[] {selections[0], selections[0]};
Check((await Send(HttpMethod.Put, $"{path}/{newProjectId}", user, Payload(duplicates))).StatusCode == HttpStatusCode.BadRequest, "duplicate repository links are rejected");
Check((await Send(HttpMethod.Put, $"{path}/{newProjectId}", user, Payload(Enumerable.Repeat(selections[0], 11).ToArray()))).StatusCode == HttpStatusCode.BadRequest, "repository link limit is enforced through MVC");
await Send(HttpMethod.Delete, "/api/integrations/github/connection", user);
await Read(await Send(HttpMethod.Put, $"{path}/{newProjectId}", user, Payload(selections)));
Check(true, "existing links can be retained after disconnecting the original account");
var removed = await Read(await Send(HttpMethod.Put, $"{path}/{newProjectId}", user, Payload([])));
Check(removed.GetProperty("gitHubRepositories").GetArrayLength() == 0, "repository links can be removed independently of project fields");
await app.StopAsync();
Console.WriteLine("GitHub integration HTTP and SQL checks passed; fake GitHub only, no real credentials used.");

static void Check(bool condition, string message) {if (!condition) throw new Exception(message); Console.WriteLine($"PASS {message}");}

sealed class FakeGitHub : HttpMessageHandler
{
    public int CodeExchanges, Refreshes;
    public bool UsedVerifier;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri!.Host == "github.com") {
            var fields = QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(ct));
            if (fields.ContainsKey("refresh_token")) Refreshes++;
            else {CodeExchanges++; UsedVerifier = fields["code_verifier"].ToString().Length == 43;}
            return Json(new {access_token = "fake-access", refresh_token = "fake-refresh", expires_in = 3600, refresh_token_expires_in = 7200});
        }
        if (request.Headers.Authorization?.Parameter != "fake-access") throw new Exception("GitHub API requests must use the scoped credential");
        return request.RequestUri.AbsolutePath switch {
            "/user" => Json(new {id = 123, login = "test-owner"}),
            "/user/installations" => Json(new {installations = new[] {new {id = 100, app_id = 42, account = new {login = "test-owner"}}, new {id = 999, app_id = 9999, account = new {login = "unrelated"}}}}),
            "/user/installations/100/repositories" => Json(new {repositories = new[] {
                new {id = 10, full_name = "test-owner/frontend", html_url = "https://github.com/test-owner/frontend", default_branch = "main", @private = false},
                new {id = 20, full_name = "test-owner/backend", html_url = "https://github.com/test-owner/backend", default_branch = "main", @private = true}
            }}),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };
    }
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) {Content = JsonContent.Create(value)};
}
