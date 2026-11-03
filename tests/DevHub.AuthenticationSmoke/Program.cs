using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DevHub.Api.Controllers;
using DevHub.Api.Realtime;
using DevHub.Api.Security;
using DevHub.Application;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Authentication.ExternalAuthentication;
using DevHub.Domain.Authentication;
using DevHub.Domain.Users;
using DevHub.Infrastructure;
using DevHub.Infrastructure.Authentication;
using DevHub.Infrastructure.GitHub;
using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using AuthenticationSchemes = DevHub.Infrastructure.Authentication.AuthenticationSchemes;

// Actual authentication, controllers, and application handlers; memory repositories only.
var store = new MemoryStore();
var upstream = new FakeProvider();
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> {
    ["ConnectionStrings:DefaultConnection"]="Server=unused;Database=unused;Integrated Security=true;TrustServerCertificate=true",
    ["Jwt:Issuer"]="oauth-audit",["Jwt:Audience"]="oauth-audit",["Jwt:Key"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
    ["Jwt:ExpirationMinutes"]="60",["Jwt:RefreshTokenExpirationDays"]="7",
    ["Authentication:Cookies:Secure"]="false",
    ["Authentication:Google:ClientId"]="audit-google-client",["Authentication:Google:ClientSecret"]="audit-google-secret",
    ["Authentication:GitHub:ClientId"]="audit-github-client",["Authentication:GitHub:ClientSecret"]="audit-github-secret",
    ["Integrations:GitHub:ClientId"]="audit-integration-client",["Integrations:GitHub:ClientSecret"]="audit-integration-secret",
    ["Integrations:GitHub:CallbackUrl"]="https://localhost:7116/api/integrations/github/callback",["Integrations:GitHub:AppSlug"]="audit-app",
    ["Frontend:BaseUrl"]="http://localhost:5173",["Frontend:AllowedOrigins:0"]="http://localhost:5173"
});
builder.Services.AddApplication().AddInfrastructure(builder.Configuration);
builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
foreach(var service in builder.Services.Where(s=>s.ServiceType==typeof(IHostedService)&&s.ImplementationType?.Name=="WorkshopUsageCleanup").ToArray()) builder.Services.Remove(service);
builder.Services.RemoveAll<IUserRepository>();builder.Services.AddSingleton<IUserRepository>(store);
builder.Services.RemoveAll<IExternalAccountRepository>();builder.Services.AddSingleton<IExternalAccountRepository>(store);
builder.Services.RemoveAll<IRefreshTokenRepository>();builder.Services.AddSingleton<IRefreshTokenRepository>(store);
builder.Services.RemoveAll<IUnitOfWork>();builder.Services.AddSingleton<IUnitOfWork>(store);
builder.Services.PostConfigure<GoogleOptions>(AuthenticationSchemes.Google,o=>o.Backchannel=new HttpClient(upstream,false));
builder.Services.PostConfigure<OAuthOptions>(AuthenticationSchemes.GitHub,o=>o.Backchannel=new HttpClient(upstream,false));
builder.Services.AddHttpClient<GitHubIntegration>().ConfigurePrimaryHttpMessageHandler(()=>upstream);
builder.Services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
builder.Services.AddSignalR();builder.Services.AddSingleton<WorkshopSubscriptions>();builder.Services.AddSingleton<WorkshopPresenceTracker>();builder.Services.AddScoped<WorkshopRealtime>();
builder.Services.AddRequestSecurity(builder.Configuration);
await using var app=builder.Build();
app.UseRouting();app.UseMiddleware<BrowserRequestSecurityMiddleware>();app.UseAuthentication();app.UseAuthorization();app.MapControllers();
app.MapGet("/authentication-smoke/jwt", () => Results.Ok()).RequireAuthorization();
await app.StartAsync();
using var client=new HttpClient(new HttpClientHandler {UseCookies=false,AllowAutoRedirect=false}) {BaseAddress=new Uri(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};
var checks=new List<object>();var failures=0;
void Check(string name,bool passed){checks.Add(new {name,passed});if(!passed)failures++;Console.WriteLine($"{(passed?"PASS":"FAIL")} {name}");}
async Task<HttpResponseMessage> Send(string route,string? cookie=null,HttpMethod? method=null,string? origin=null){
    using var request=new HttpRequestMessage(method??HttpMethod.Get,route);
    if(cookie is not null)request.Headers.Add("Cookie",cookie);
    if(origin is not null)request.Headers.Add("Origin",origin);
    return await client.SendAsync(request);
}
string Cookies(HttpResponseMessage response,string contains)=>string.Join("; ",response.Headers.TryGetValues("Set-Cookie",out var values)?values.Select(v=>v.Split(';')[0]).Where(v=>v.Contains(contains,StringComparison.Ordinal)&&!v.EndsWith('=')):[]);
var cookieOptions=app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(CookieAuthenticationDefaults.AuthenticationScheme);
var externalOptions=app.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get(AuthenticationSchemes.ExternalCookie);
string Session(User user){
    var principal=new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub",user.Id.ToString()),new Claim(ClaimTypes.NameIdentifier,user.Id.ToString())],CookieAuthenticationDefaults.AuthenticationScheme));
    return $"{cookieOptions.Cookie.Name}={cookieOptions.TicketDataFormat.Protect(new AuthenticationTicket(principal,new AuthenticationProperties {IssuedUtc=DateTimeOffset.UtcNow,ExpiresUtc=DateTimeOffset.UtcNow.AddMinutes(20)},CookieAuthenticationDefaults.AuthenticationScheme))}";
}
async Task<Flow> Start(string provider,string? session=null,bool linking=false){
    using var response=await Send(linking?$"/api/users/me/connections/{provider}":$"/api/auth/external/{provider}",session);
    if(response.StatusCode!=HttpStatusCode.Redirect)throw new Exception($"Local challenge failed: {(int)response.StatusCode}");
    var q=QueryHelpers.ParseQuery(response.Headers.Location!.Query);
    return new(provider,q["state"].ToString(),q["code_challenge"].ToString(),Cookies(response,".AspNetCore.Correlation."));
}
string Code(Flow flow){var code=Guid.NewGuid().ToString("N");upstream.Codes[code]=flow.Challenge;return code;}
async Task<HttpResponseMessage> Callback(Flow flow,string? cookie=null,string? code=null,string? state=null,string? provider=null)=>await Send($"/signin-{provider??flow.Provider}?code={code??Code(flow)}&state={Uri.EscapeDataString(state??flow.State)}",cookie??flow.Cookie);
async Task<HttpResponseMessage> Complete(HttpResponseMessage callback,string? session=null){
    var external=Cookies(callback,"__Host-DevHub.External=");
    if(external.Length==0)throw new Exception("Positive OAuth callback did not issue external cookie.");
    return await Send("/api/auth/external/callback",external+(session is null?"":"; "+session));
}
bool HasError(HttpResponseMessage response,string error)=>response.Headers.Location is {} uri&&QueryHelpers.ParseQuery(uri.Query)["oauthError"]==error;
try{
    var googleOptions=app.Services.GetRequiredService<IOptionsMonitor<GoogleOptions>>().Get(AuthenticationSchemes.Google);
    var githubOptions=app.Services.GetRequiredService<IOptionsMonitor<OAuthOptions>>().Get(AuthenticationSchemes.GitHub);
    await JwtChecks.Run(app.Services,client,Check);
    Check("provider tokens are not saved to external authentication tickets",!googleOptions.SaveTokens&&!githubOptions.SaveTokens);
    Check("external ticket is Secure, HttpOnly, SameSite=Lax and ten minutes",externalOptions.Cookie.SecurePolicy==CookieSecurePolicy.Always&&externalOptions.Cookie.HttpOnly&&externalOptions.Cookie.SameSite==SameSiteMode.Lax&&externalOptions.ExpireTimeSpan==TimeSpan.FromMinutes(10));
    foreach(var provider in new[]{"google","github"}){
        upstream.ResetProfile();
        var flow=await Start(provider);
        var properties=provider=="google"?googleOptions.StateDataFormat.Unprotect(flow.State):githubOptions.StateDataFormat.Unprotect(flow.State);
        Check($"{provider}: protected state binds provider and return path",properties?.Items[ExternalAuthenticationConstants.ProviderItem]==provider&&properties.Items[ExternalAuthenticationConstants.ReturnUrlItem]=="/workshop");
        var count=upstream.Exchanges;
        using(var response=await Callback(flow,state:flow.State[..^3]+"abc"))Check($"{provider}: tampered state rejected before token exchange",upstream.Exchanges==count&&HasError(response,"provider_rejected"));
        using(var response=await Callback(flow,cookie:"unrelated=value"))Check($"{provider}: missing correlation cookie rejected before token exchange",upstream.Exchanges==count&&HasError(response,"provider_rejected"));
        using(var response=await Callback(flow,provider:provider=="google"?"github":"google"))Check($"{provider}: cross-provider state rejected before exchange",upstream.Exchanges==count&&HasError(response,"provider_rejected"));
        var other=await Start(provider);
        using(var response=await Callback(flow,cookie:other.Cookie))Check($"{provider}: another browser flow's correlation cookie rejected",upstream.Exchanges==count&&HasError(response,"provider_rejected"));
        var wrongCode=Code(other);
        using(var response=await Callback(flow,code:wrongCode))Check($"{provider}: substituted code is rejected by simulated PKCE provider",upstream.PkceRejections>0&&HasError(response,"provider_rejected"));
        using var success=await Callback(flow);
        Check($"{provider}: valid correlated callback reaches completion",success.StatusCode==HttpStatusCode.Redirect&&success.Headers.Location?.OriginalString=="/api/auth/external/callback"&&Cookies(success,"__Host-DevHub.External=").Length>0);
        Check($"{provider}: token request uses matching verifier and fixed callback",upstream.LastVerifierValid&&upstream.LastRedirect?.EndsWith($"/signin-{provider}",StringComparison.Ordinal)==true);
        Check($"{provider}: code exchange authenticates the confidential client",upstream.LastFields.GetValueOrDefault("client_id")==$"audit-{provider}-client"&&upstream.LastFields.GetValueOrDefault("client_secret")==$"audit-{provider}-secret"&&upstream.LastFields.GetValueOrDefault("grant_type")=="authorization_code");
        var extCookie=Cookies(success,"__Host-DevHub.External=").Split('=',2)[1];
        var ticket=externalOptions.TicketDataFormat.Unprotect(Uri.UnescapeDataString(extCookie));
        Check($"{provider}: external ticket omits upstream access/refresh tokens",ticket is not null&&!ticket.Properties.GetTokens().Any());
        Check($"{provider}: verified email reaches external ticket",ticket?.Principal.FindFirstValue(ExternalAuthenticationConstants.EmailVerifiedClaim)==bool.TrueString||ticket?.Principal.FindFirstValue(ExternalAuthenticationConstants.EmailVerifiedClaim)=="true");
        using var completed=await Complete(success);
        Check($"{provider}: successful sign-in creates only a memory-store account",completed.StatusCode==HttpStatusCode.Redirect&&!completed.Headers.Location!.Query.Contains("oauthError")&&Cookies(completed,"DevHub.Auth=").Length>0);
        Check($"{provider}: provider code/token absent from final redirect",!completed.Headers.Location!.OriginalString.Contains("access_token",StringComparison.OrdinalIgnoreCase)&&!completed.Headers.Location.OriginalString.Contains("code=",StringComparison.OrdinalIgnoreCase));
    }
    upstream.ResetProfile();upstream.GoogleVerified=false;
    using(var callbackResponse=await Callback(await Start("google")))using(var completion=await Complete(callbackResponse))Check("Google unverified email cannot complete sign-in",HasError(completion,"verified_email_required"));
    upstream.ResetProfile();upstream.GitHubPublicEmail=null;upstream.GitHubPrivateVerified=false;
    using(var callbackResponse=await Callback(await Start("github")))using(var completion=await Complete(callbackResponse))Check("GitHub without a verified private email cannot sign in",HasError(completion,"verified_email_required"));
    upstream.ResetProfile();upstream.GitHubPublicEmail=null;
    using(var callbackResponse=await Callback(await Start("github")))using(var completion=await Complete(callbackResponse))Check("GitHub verified private-email fallback can sign in",!completion.Headers.Location!.Query.Contains("oauthError"));
    upstream.ResetProfile();upstream.GitHubPrivateVerified=false;var emailCalls=upstream.EmailRequests;
    using(var callbackResponse=await Callback(await Start("github")))using(var completion=await Complete(callbackResponse))
        Check("GitHub public email cannot bypass explicit verification",upstream.EmailRequests==emailCalls+1&&HasError(completion,"verified_email_required"));
    upstream.ResetProfile();
    var alice=User.Create("alice@example.invalid","alice","Alice","Audit","test-hash",DateTimeOffset.UtcNow);
    var bob=User.Create("bob@example.invalid","bob","Bob","Audit","test-hash",DateTimeOffset.UtcNow);
    store.Add(alice);store.Add(bob);var aliceSession=Session(alice);var bobSession=Session(bob);
    var linking=await Start("github",aliceSession,true);
    using(var callbackResponse=await Callback(linking)){
        var before=store.Accounts.Count;
        using(var completion=await Complete(callbackResponse,bobSession))Check("link callback rejects a different local account",HasError(completion,"linking_session_expired")&&store.Accounts.Count==before);
        using(var completion=await Complete(callbackResponse))Check("link callback rejects absent local session",HasError(completion,"linking_session_expired")&&store.Accounts.Count==before);
        using(var completion=await Complete(callbackResponse,aliceSession))Check("link callback accepts the initiating account",!completion.Headers.Location!.Query.Contains("oauthError")&&store.Accounts.Any(a=>a.UserId==alice.Id&&a.Provider=="github"));
    }
    using var scope=app.Services.CreateScope();var sender=scope.ServiceProvider.GetRequiredService<ISender>();
    var collision=await sender.Send(new ExternalSignInCommand(new("google","new-unlinked-id",alice.Email,null,"Audit","User",null)));
    Check("email collision cannot silently merge into existing local account",!collision.IsSuccess&&collision.Error?.Code=="Authentication.ExistingAccountMustBeLinked");
    var aliceGitHub=store.Accounts.Single(a=>a.UserId==alice.Id&&a.Provider=="github");
    var takeover=await sender.Send(new LinkExternalAccountCommand(bob.Id,new("github",aliceGitHub.ProviderUserId,"unused@example.invalid",null,"Audit","User",null)));
    Check("provider identity already owned by another user cannot be linked",!takeover.IsSuccess&&takeover.Error?.Code=="Authentication.ExternalAccountAlreadyLinked");
    var duplicate=await sender.Send(new LinkExternalAccountCommand(alice.Id,new("github","second-github-identity","unused@example.invalid",null,"Audit","User",null)));
    Check("second identity for same provider cannot replace existing link",!duplicate.IsSuccess&&duplicate.Error?.Code=="Authentication.ProviderAlreadyLinked");
    var passwordless=User.CreateExternal("passwordless@example.invalid","passwordless","Audit","User",null,DateTimeOffset.UtcNow);store.Add(passwordless);store.Add(ExternalAccount.Create(passwordless.Id,"google","passwordless-id",null,passwordless.Email,null,DateTimeOffset.UtcNow));
    var unlink=await sender.Send(new DisconnectExternalAccountCommand(passwordless.Id,"google"));
    Check("disconnect cannot remove the final passwordless sign-in method",!unlink.IsSuccess&&unlink.Error?.Code=="Authentication.FinalSignInMethod");
    using(var blocked=await Send("/api/integrations/github/authorize",aliceSession,HttpMethod.Post,"https://evil.example"))Check("repository authorization rejects a foreign browser origin",blocked.StatusCode==HttpStatusCode.Forbidden);
    using(var blocked=await Send("/api/users/me/connections/github",aliceSession,HttpMethod.Delete,"https://evil.example"))Check("unlink rejects a foreign browser origin",blocked.StatusCode==HttpStatusCode.Forbidden);
    upstream.DenyIntegrationExchange=true;
    foreach(var install in new[]{false,true}){
        using var start=await Send($"/api/integrations/github/authorize?install={install.ToString().ToLowerInvariant()}",aliceSession,HttpMethod.Post);
        var data=await start.Content.ReadFromJsonAsync<JsonElement>();var authUri=new Uri(data.GetProperty("url").GetString()!);var q=QueryHelpers.ParseQuery(authUri.Query);
        var cookie=Cookies(start,"__Host-DevHub.GitHubIntegration=");var state=q["state"].ToString();var count=upstream.Exchanges;
        var cb=$"/api/integrations/github/callback?code=audit-integration-code&state={state}";
        using(var bad=await Send(cb.Replace(state,"wrong-state"),aliceSession+"; "+cookie))Check($"repository install={install}: bad state prevents exchange",upstream.Exchanges==count&&bad.Headers.Location!.Query.Contains("failed"));
        using(var bad=await Send(cb,bobSession+"; "+cookie))Check($"repository install={install}: wrong local account prevents exchange",upstream.Exchanges==count&&bad.Headers.Location!.Query.Contains("failed"));
        using(var bad=await Send(cb,aliceSession))Check($"repository install={install}: absent flow cookie prevents exchange",upstream.Exchanges==count&&bad.Headers.Location!.Query.Contains("failed"));
        using var valid=await Send(cb,aliceSession+"; "+cookie);
        if(!install){
            Check("repository ordinary callback reaches simulated provider",upstream.Exchanges==count+1&&valid.Headers.Location!.Query.Contains("failed"));
            Check("repository ordinary flow sends S256 verifier",q["code_challenge_method"]=="S256"&&upstream.LastFields.TryGetValue("code_verifier",out var verifier)&&WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))==q["code_challenge"]);
        }else{
            var next=valid.Headers.Location;
            Check("installation callback never exchanges its unbound code",upstream.Exchanges==count&&next?.Host=="github.com"&&next.AbsolutePath=="/login/oauth/authorize");
            var nextQuery=QueryHelpers.ParseQuery(next!.Query);
            var nextCookie=Cookies(valid,"__Host-DevHub.GitHubIntegration=");
            Check("installation starts fresh state and S256 authorization",nextQuery["state"]!=state&&nextQuery["code_challenge_method"]=="S256"&&nextQuery["code_challenge"].ToString().Length==43&&nextCookie!=cookie);
            using(var stale=await Send(cb,aliceSession+"; "+nextCookie))Check("installation state cannot complete the fresh authorization",upstream.Exchanges==count&&stale.Headers.Location!.Query.Contains("failed"));
            using var completed=await Send($"/api/integrations/github/callback?code=audit-fresh-code&state={nextQuery["state"]}",aliceSession+"; "+nextCookie);
            Check("post-install code exchange requires matching PKCE verifier",upstream.Exchanges==count+1&&upstream.LastFields.GetValueOrDefault("code")=="audit-fresh-code"&&upstream.LastFields.TryGetValue("code_verifier",out var verifier)&&WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))==nextQuery["code_challenge"]&&completed.Headers.Location!.Query.Contains("failed"));
        }
    }
    var flowProtector=app.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("DevHub.GitHub.OAuthState.v1");
    foreach(var (name,verifier,until) in new[]{("legacy flow without PKCE","",DateTimeOffset.UtcNow.AddMinutes(5)),("expired flow",new string('a',43),DateTimeOffset.UtcNow.AddMinutes(-1))}){
        var protectedFlow=flowProtector.Protect(JsonSerializer.Serialize(new {UserId=alice.Id,State="audit-state",Verifier=verifier,Until=until}));
        var count=upstream.Exchanges;
        using var rejected=await Send("/api/integrations/github/callback?code=audit-code&state=audit-state",aliceSession+"; __Host-DevHub.GitHubIntegration="+protectedFlow);
        Check($"repository {name} cannot exchange a code",upstream.Exchanges==count&&rejected.Headers.Location!.Query.Contains("failed"));
    }
    var beforeInvalidVerifier=upstream.Exchanges;var verifierRejected=false;
    try {await scope.ServiceProvider.GetRequiredService<GitHubIntegration>().Connect(alice.Id,"audit-code","",default);}
    catch(GitHubIntegrationException){verifierRejected=true;}
    Check("integration service rejects missing verifier before upstream exchange",verifierRejected&&upstream.Exchanges==beforeInvalidVerifier);
    Console.WriteLine($"Completed {checks.Count} checks; {failures} failed expectations. Fake providers and memory repositories only.");
}finally{await app.StopAsync();}
return failures==0?0:1;

sealed record Flow(string Provider,string State,string Challenge,string Cookie);
sealed class MemoryStore:IUserRepository,IExternalAccountRepository,IRefreshTokenRepository,IUnitOfWork{
    public List<User> Users {get;}=[];public List<ExternalAccount> Accounts {get;}=[];private readonly List<RefreshToken> refresh=[];
    public Task<User?> GetByIdAsync(Guid id,CancellationToken cancellationToken=default)=>Task.FromResult(Users.SingleOrDefault(u=>u.Id==id));
    public Task<User?> GetByIdForUpdateAsync(Guid id,CancellationToken cancellationToken=default)=>GetByIdAsync(id,cancellationToken);
    public Task<User?> GetByEmailAsync(string email,CancellationToken cancellationToken=default)=>Task.FromResult(Users.SingleOrDefault(u=>u.Email.Equals(email,StringComparison.OrdinalIgnoreCase)));
    public Task<bool> UsernameExistsAsync(string username,Guid? excludingUserId=null,CancellationToken cancellationToken=default)=>Task.FromResult(Users.Any(u=>u.Id!=excludingUserId&&u.Username.Equals(username,StringComparison.OrdinalIgnoreCase)));
    public void Add(User user){Users.Add(user);foreach(var account in user.ExternalAccounts)Accounts.Add(account);}
    public Task<ExternalAccount?> GetByProviderIdentityAsync(string provider,string providerUserId,CancellationToken cancellationToken=default)=>Task.FromResult(Accounts.SingleOrDefault(a=>a.Provider==provider&&a.ProviderUserId==providerUserId));
    public Task<ExternalAccount?> GetByUserAndProviderAsync(Guid userId,string provider,CancellationToken cancellationToken=default)=>Task.FromResult(Accounts.SingleOrDefault(a=>a.UserId==userId&&a.Provider==provider));
    public void Add(ExternalAccount account){Accounts.Add(account);Users.Single(u=>u.Id==account.UserId).AddExternalAccount(account);}
    public void Remove(ExternalAccount account){Accounts.Remove(account);Users.Single(u=>u.Id==account.UserId).RemoveExternalAccount(account);}
    public Task<RefreshToken?> GetByHashAsync(string tokenHash,CancellationToken cancellationToken=default)=>Task.FromResult(refresh.SingleOrDefault(t=>t.TokenHash==tokenHash));
    public Task RevokeFamilyAsync(Guid familyId,DateTimeOffset revokedAtUtc,CancellationToken cancellationToken=default)=>Task.CompletedTask;
    public void Add(RefreshToken token)=>refresh.Add(token);
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken=default)=>Task.FromResult(1);
}
sealed class FakeProvider:HttpMessageHandler{
    public Dictionary<string,string> Codes {get;}=[];
    public int Exchanges,PkceRejections,EmailRequests;public bool LastVerifierValid,GoogleVerified=true,GitHubPrivateVerified=true,DenyIntegrationExchange;
    public string? GitHubPublicEmail="github-audit@example.invalid",LastRedirect;public Dictionary<string,string> LastFields=[];
    private long githubId=100;private string googleId=Guid.NewGuid().ToString("N");
    public void ResetProfile(){GoogleVerified=true;GitHubPrivateVerified=true;GitHubPublicEmail="github-audit-"+(++githubId)+"@example.invalid";googleId=Guid.NewGuid().ToString("N");}
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
        var uri=request.RequestUri!;
        if(request.Method==HttpMethod.Post){
            Exchanges++;var fields=QueryHelpers.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));LastFields=fields.ToDictionary(p=>p.Key,p=>p.Value.ToString());
            LastRedirect=fields["redirect_uri"];var verifier=fields.TryGetValue("code_verifier",out var verifierValue)?verifierValue.ToString():"";var code=fields["code"].ToString();
            if(fields["client_id"]=="audit-integration-client"&&DenyIntegrationExchange)return new(HttpStatusCode.BadRequest){Content=JsonContent.Create(new {error="invalid_grant"})};
            LastVerifierValid=Codes.TryGetValue(code,out var challenge)&&verifier.Length>0&&WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)))==challenge;
            if(!LastVerifierValid){PkceRejections++;return new(HttpStatusCode.BadRequest){Content=JsonContent.Create(new {error="invalid_grant"})};}
            Codes.Remove(code);return new(HttpStatusCode.OK){Content=JsonContent.Create(new {access_token="fake-provider-access",refresh_token="fake-provider-refresh",token_type="Bearer",expires_in=3600})};
        }
        if(uri.Host=="api.github.com"&&uri.AbsolutePath=="/user/emails"){
            EmailRequests++;return new(HttpStatusCode.OK){Content=JsonContent.Create(new[]{new {email=$"private-{githubId}@example.invalid",verified=GitHubPrivateVerified,primary=true}})};
        }
        if(uri.Host=="api.github.com"&&uri.AbsolutePath=="/user")return new(HttpStatusCode.OK){Content=JsonContent.Create(new {id=githubId,login="audit-github-"+githubId,name="Audit GitHub",email=GitHubPublicEmail})};
        if(uri.Host.EndsWith("googleapis.com",StringComparison.Ordinal))return new(HttpStatusCode.OK){Content=JsonContent.Create(new {id=googleId,sub=googleId,email="google-audit-"+googleId+"@example.invalid",email_verified=GoogleVerified,name="Audit Google",given_name="Audit",family_name="Google"})};
        throw new InvalidOperationException("Unexpected simulated upstream request; no network transport is available.");
    }
}
