using System.Security.Claims;
using DevHub.Api.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

internal static class PlatformAdminChecks
{
    public static async Task RunAsync(Action<bool, string> check)
    {
        var approvedId = Guid.NewGuid();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PlatformAdmin:Origin"] = "https://admin.dev-hub.site",
            ["PlatformAdmin:UserIds:0"] = approvedId.ToString(),
            ["Frontend:AllowedOrigins:0"] = "https://dev-hub.site",
        }).Build();
        var access = new PlatformAdminAccess(configuration);
        using var services = new ServiceCollection().AddLogging()
            .AddAuthorization(options => options.AddPolicy(PlatformAdminAccess.Policy, access.ConfigurePolicy))
            .BuildServiceProvider();
        var authorization = services.GetRequiredService<IAuthorizationService>();
        var policies = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await policies.GetPolicyAsync(PlatformAdminAccess.Policy);
        check(policy?.AuthenticationSchemes.SequenceEqual([CookieAuthenticationDefaults.AuthenticationScheme]) == true,
            "platform admin accepts the authenticated cookie scheme only");

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("admin.dev-hub.site");
        var approved = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", approvedId.ToString())], "Cookies"));
        var other = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "Cookies"));
        async Task<bool> CanRead(ClaimsPrincipal user) =>
            (await authorization.AuthorizeAsync(user, context, PlatformAdminAccess.Policy)).Succeeded;
        check(await CanRead(approved), "approved account can read the admin overview on its HTTPS hostname");
        check(!await CanRead(other) && !await CanRead(new ClaimsPrincipal()), "regular and anonymous users cannot read admin data");
        context.Request.Host = new HostString("dev-hub.site");
        check(!await CanRead(approved), "admin data is unavailable through the main hostname");
        context.Request.Host = new HostString("admin.dev-hub.site");
        context.Request.Scheme = "http";
        check(!await CanRead(approved), "the production admin policy requires HTTPS");

        var browser = new BrowserOriginPolicy(configuration, access);
        context.Request.Scheme = "https";
        context.Request.Path = "/api/auth/login";
        context.Request.Method = HttpMethods.Post;
        context.Request.Headers.Origin = "https://admin.dev-hub.site";
        check(browser.IsAllowed(context.Request), "admin sign-in accepts the admin origin");
        context.Request.Headers.Origin = "https://dev-hub.site";
        check(!browser.IsAllowed(context.Request), "the main site cannot perform admin authentication writes");
        context.Request.Host = new HostString("dev-hub.site");
        check(browser.IsAllowed(context.Request), "main-site authentication still accepts its configured origin");
    }
}
