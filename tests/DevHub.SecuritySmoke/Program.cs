using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Security.Claims;
using DevHub.Api.BugReports;
using DevHub.Api.Security;
using DevHub.Application.Authentication.Commands.Login;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc;
using DevHub.Infrastructure;

var checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    Console.WriteLine($"PASS {message}"); checks++;
}
await PlatformAdminChecks.RunAsync(Check);
if (args is ["--platform-admin-only"]) return;

var builder = WebApplication.CreateBuilder();
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["Frontend:AllowedOrigins:0"] = "https://dev-hub.site",
    ["Security:Limits:RequestsPerMinute"] = "1000",
    ["Security:Limits:WritesPerMinute"] = "1000",
    ["Security:Limits:LoginsPerMinute"] = "2",
    ["Security:Limits:RegistrationsPerHour"] = "2",
    ["Security:Limits:RefreshesPerMinute"] = "2",
    ["Security:Limits:ConcurrentRequests"] = "2",
    ["Security:Limits:ConcurrentUploads"] = "1",
    ["Security:Workshop:SavesPerMinute"] = "2",
});
builder.Services.AddBugReportRateLimiting();
builder.Services.AddRequestSecurity(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddControllers().ConfigureApplicationPartManager(parts => parts.ApplicationParts.Clear())
    .AddApplicationPart(typeof(BudgetController).Assembly);
await using var app = builder.Build();
app.UseExceptionHandler(new ExceptionHandlerOptions {StatusCodeSelector = RequestSecurity.ExceptionStatusCode});
app.UseWebSockets();
app.UseRouting();
app.UseMiddleware<BrowserRequestSecurityMiddleware>();
app.UseRateLimiter();
app.MapControllers();
var completed = 0;
foreach (var action in new[] {"login", "register", "refresh"})
    app.MapPost($"/api/auth/{action}", () => {Interlocked.Increment(ref completed); return Results.Unauthorized();})
        .RequireRateLimiting($"auth-{action}");
app.MapPost("/api/write", () => {Interlocked.Increment(ref completed); return Results.Ok();});
app.MapPut("/api/projects/{projectId}/tools/{tool}", () => Results.Ok()).RequireRateLimiting("workshop-save");
app.MapGet("/hubs/workshop", () => Results.Ok());
var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
app.MapPost("/api/upload", async () => {started.TrySetResult(); await release.Task; return Results.Ok();});
await app.StartAsync();
using var client = new HttpClient {BaseAddress = new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
try
{
    for (var index = 0; index < 2; index++)
        using (var response = await client.PutAsync("/api/projects/one/tools/notes", new StringContent("{}")))
            Check(response.IsSuccessStatusCode, "workshop save within its document budget succeeds");
    using (var response = await client.PutAsync("/api/projects/one/tools/notes", new StringContent("{}")))
        Check(response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.CacheControl?.NoStore == true, "workshop document save excess is throttled without caching");
    using (var response = await client.PutAsync("/api/projects/two/tools/notes", new StringContent("{}")))
        Check(response.IsSuccessStatusCode, "one document's save budget does not block another project");
    using (var response = await client.PutAsync("/api/projects/one/tools/wiki", new StringContent("{}")))
        Check(response.IsSuccessStatusCode, "separate tools have separate save budgets");
    foreach (var action in new[] {"login", "register", "refresh"})
    {
        for (var index = 0; index < 2; index++)
            using (var response = await client.PostAsync($"/api/auth/{action}", new StringContent("{}")))
                Check(response.StatusCode == HttpStatusCode.Unauthorized, $"{action}: normal request reaches endpoint");
        using var rejected = await client.PostAsync($"/api/auth/{action}", new StringContent("{}"));
        Check(rejected.StatusCode == HttpStatusCode.TooManyRequests, $"{action}: excess request rejected before endpoint");
        Check(rejected.Headers.RetryAfter is not null && rejected.Headers.CacheControl?.NoStore == true, $"{action}: retry and no-store headers supplied");
    }
    using (var spoof = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login"))
    {
        spoof.Headers.Add("X-Forwarded-For", "198.51.100.40");
        using var response = await client.SendAsync(spoof);
        Check(response.StatusCode == HttpStatusCode.TooManyRequests, "unprocessed forwarding headers cannot change limiter identity");
    }
    foreach (var origin in new[] {"https://evil.example", "https://dev-hub.site.evil.example", "null", "https://dev-hub.site@evil.example"})
    {
        var before = completed;
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/write");
        request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request);
        Check(response.StatusCode == HttpStatusCode.Forbidden && completed == before, $"untrusted origin rejected before side effects: {origin}");
    }
    using (var trusted = new HttpRequestMessage(HttpMethod.Post, "/api/write"))
    {
        trusted.Headers.Add("Origin", "https://dev-hub.site");
        using var response = await client.SendAsync(trusted);
        Check(response.IsSuccessStatusCode && response.Headers.CacheControl?.NoStore == true, "trusted browser write succeeds without response caching");
    }
    using (var crossSite = new HttpRequestMessage(HttpMethod.Post, "/api/write"))
    {
        crossSite.Headers.Add("Sec-Fetch-Site", "cross-site");
        using var response = await client.SendAsync(crossSite);
        Check(response.StatusCode == HttpStatusCode.Forbidden, "cross-site Fetch Metadata rejected when Origin is missing");
    }
    using (var hubRequest = new HttpRequestMessage(HttpMethod.Get, "/hubs/workshop"))
    {
        hubRequest.Headers.Add("Origin", "https://evil.example");
        using var response = await client.SendAsync(hubRequest);
        Check(response.StatusCode == HttpStatusCode.Forbidden, "cross-origin realtime handshake rejected independently of CORS");
    }
    var upload = client.PostAsync("/api/upload", new StringContent("field=value", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded"));
    await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
    using (var excessive = await client.PostAsync("/api/upload", new StringContent("field=value", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")))
        Check(excessive.StatusCode == HttpStatusCode.TooManyRequests, "parallel upload memory budget is enforced");
    release.TrySetResult();
    using (var successful = await upload) Check(successful.IsSuccessStatusCode, "existing upload completes after another upload is rejected");
    using (var another = await client.PostAsync("/api/upload", new StringContent("field=value", System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")))
        Check(another.IsSuccessStatusCode, "upload permit released after completion");

    using (var invalidLogin = await client.PostAsync("/api/budget/login-validation", new StringContent(
        System.Text.Json.JsonSerializer.Serialize(new LoginCommand(new string('a', 321), new string('b', 129))), System.Text.Encoding.UTF8, "application/json")))
        Check(invalidLogin.StatusCode == HttpStatusCode.BadRequest, "real MVC login record validation rejects oversized credentials");
    using (var validLogin = await client.PostAsync("/api/budget/login-validation", new StringContent(
        System.Text.Json.JsonSerializer.Serialize(new LoginCommand("unused@example.invalid", "normal-password")), System.Text.Encoding.UTF8, "application/json")))
        Check(validLogin.IsSuccessStatusCode, "real MVC login record validation accepts normal credentials");
    var largeBody = "{\"text\":\"" + new string('x', 1_100_000) + "\"}";
    using (var bounded = await client.PostAsync("/api/budget/default", new StringContent(largeBody, System.Text.Encoding.UTF8, "application/json")))
        Check(bounded.StatusCode == HttpStatusCode.RequestEntityTooLarge, "default API body limit enforced before model binding");
    using (var document = await client.PostAsync("/api/budget/document", new StringContent(largeBody, System.Text.Encoding.UTF8, "application/json")))
        Check(document.IsSuccessStatusCode, "explicit document body limit overrides the default");

    var authenticationConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:DefaultConnection"] = "Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true",
        ["Jwt:Issuer"] = "security-test", ["Jwt:Audience"] = "security-test",
        ["Jwt:Key"] = new string('x', 64), ["Jwt:ExpirationMinutes"] = "15", ["Jwt:RefreshTokenExpirationDays"] = "7",
    }).Build();
    var authenticationBuilder = Host.CreateApplicationBuilder();
    authenticationBuilder.Services.AddInfrastructure(authenticationConfig);
    using var authenticationHost = authenticationBuilder.Build();
    var cookie = authenticationHost.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
    Check(!cookie.SlidingExpiration && cookie.ExpireTimeSpan == TimeSpan.FromMinutes(15), "cookie authentication cannot silently extend a short access session");
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "alice")], "test"));
    var scheme = new AuthenticationScheme(CookieAuthenticationDefaults.AuthenticationScheme, null, typeof(CookieAuthenticationHandler));
    var oldTicket = new AuthenticationTicket(principal, new AuthenticationProperties
        {IssuedUtc = DateTimeOffset.UtcNow.AddHours(-1), ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)}, scheme.Name);
    var oldContext = new CookieValidatePrincipalContext(new DefaultHttpContext(), scheme, cookie, oldTicket);
    await cookie.Events.ValidatePrincipal(oldContext);
    Check(oldContext.Principal is null, "legacy long-lived authentication cookies are rejected after the access lifetime");
    var freshTicket = new AuthenticationTicket(principal, new AuthenticationProperties
        {IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(15)}, scheme.Name);
    var freshContext = new CookieValidatePrincipalContext(new DefaultHttpContext(), scheme, cookie, freshTicket);
    await cookie.Events.ValidatePrincipal(freshContext);
    Check(freshContext.Principal is not null, "fresh authentication cookies remain valid");
    using var authenticationScope = authenticationHost.Services.CreateScope();
    var passwords = authenticationScope.ServiceProvider.GetRequiredService<DevHub.Application.Abstractions.Authentication.IPasswordHasher>();
    var hash = passwords.Hash("test-password-with-enough-length");
    Check(passwords.Verify(hash, "test-password-with-enough-length"), "real password verification still succeeds");
    Check(!passwords.Verify(hash, "incorrect-password"), "incorrect passwords remain rejected");
    Check(!passwords.Verify(string.Empty, "test-password-with-enough-length"), "missing-account dummy verification cannot authenticate a password");
    using var filter = new RealtimeAbuseFilter();
    var caller = new TestCaller("alice", "tab-one");
    var hub = new TestHub();
    var method = typeof(TestHub).GetMethod(nameof(TestHub.SendMessage))!;
    var invocation = new HubInvocationContext(caller, app.Services, hub, method, []);
    for (var index = 0; index < 5; index++) await filter.InvokeMethodAsync(invocation, _ => ValueTask.FromResult<object?>("ok"));
    var blocked = false;
    try {await filter.InvokeMethodAsync(new(new TestCaller("alice", "tab-two"), app.Services, hub, method, []), _ => ValueTask.FromResult<object?>("ok"));}
    catch (HubException) {blocked = true;}
    Check(blocked, "chat spam budget shared across connections for the same user");
    Check((string?)await filter.InvokeMethodAsync(new(new TestCaller("bob", "other-tab"), app.Services, hub, method, []), _ => ValueTask.FromResult<object?>("ok")) == "ok", "one user's spam does not consume another user's chat allowance");
    var finishUserWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var userWork = Enumerable.Range(0, 4).Select(index => filter.InvokeMethodAsync(
        new(new TestCaller("busy-user", $"busy-{index}"), app.Services, hub, method, []),
        async _ => {await finishUserWork.Task; return "ok";}).AsTask()).ToArray();
    blocked = false;
    try {await filter.InvokeMethodAsync(new(new TestCaller("busy-user", "fifth-tab"), app.Services, hub, method, []), _ => ValueTask.FromResult<object?>("ok"));} catch (HubException) {blocked = true;}
    finishUserWork.TrySetResult(); await Task.WhenAll(userWork);
    Check(blocked, "a user cannot hold more than four concurrent realtime operations");
    var finishServerWork = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    var serverWork = Enumerable.Range(0, 16).Select(index => filter.InvokeMethodAsync(
        new(new TestCaller($"capacity-user-{index}", $"capacity-tab-{index}"), app.Services, hub, method, []),
        async _ => {await finishServerWork.Task; return "ok";}).AsTask()).ToArray();
    blocked = false;
    try {await filter.InvokeMethodAsync(new(new TestCaller("seventeenth-user", "seventeenth-tab"), app.Services, hub, method, []), _ => ValueTask.FromResult<object?>("ok"));} catch (HubException) {blocked = true;}
    finishServerWork.TrySetResult(); await Task.WhenAll(serverWork);
    Check(blocked, "server-wide realtime work is bounded across different users");
    Check((string?)await filter.InvokeMethodAsync(new(new TestCaller("after-capacity", "after-capacity-tab"), app.Services, hub, method, []), _ => ValueTask.FromResult<object?>("ok")) == "ok", "realtime work permits released after completion");
    var connections = Enumerable.Range(0, 21).Select(index => new HubLifetimeContext(new TestCaller("charlie", $"tab-{index}"), app.Services, hub)).ToArray();
    foreach (var connection in connections.Take(20)) await filter.OnConnectedAsync(connection, _ => Task.CompletedTask);
    blocked = false;
    try {await filter.OnConnectedAsync(connections[20], _ => Task.CompletedTask);} catch (HubException) {blocked = true;}
    Check(blocked, "realtime connection count is bounded per user");
    await filter.OnDisconnectedAsync(connections[0], null, (_, _) => Task.CompletedTask);
    await filter.OnConnectedAsync(connections[20], _ => Task.CompletedTask);
    Check(true, "realtime connection permit is released on disconnect");
    foreach (var connection in connections.Skip(1)) await filter.OnDisconnectedAsync(connection, null, (_, _) => Task.CompletedTask);
    Console.WriteLine($"PASS {checks} security checks; no database or production traffic used");
}
finally {release.TrySetResult(); await app.StopAsync();}

sealed class TestHub : Hub { public void SendMessage() {} }
[Microsoft.AspNetCore.Mvc.Route("api/budget")]
public sealed class BudgetController : DevHub.Api.Controllers.ApiControllerBase
{
    [Microsoft.AspNetCore.Mvc.HttpPost("login-validation")]
    public IActionResult LoginValidation(LoginCommand credentials) => Ok();
    [Microsoft.AspNetCore.Mvc.HttpPost("default")]
    public IActionResult Default(System.Text.Json.JsonElement body) => Ok();
    [Microsoft.AspNetCore.Mvc.HttpPost("document"), Microsoft.AspNetCore.Mvc.RequestSizeLimit(2_000_000)]
    public IActionResult Document(System.Text.Json.JsonElement body) => Ok();
}
sealed class TestCaller(string user, string connection) : HubCallerContext
{
    public override string ConnectionId => connection;
    public override string? UserIdentifier => user;
    public override ClaimsPrincipal? User => new(new ClaimsIdentity([new Claim("sub", user)], "test"));
    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
    public override IFeatureCollection Features { get; } = new FeatureCollection();
    public override CancellationToken ConnectionAborted => CancellationToken.None;
    public override void Abort() {}
}
