using System.Text;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using DevHub.Application.Abstractions.Authentication;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Infrastructure.Authentication;
using DevHub.Infrastructure.Persistence;
using DevHub.Infrastructure.Persistence.Repositories;
using DevHub.Application.Chats;
using DevHub.Infrastructure.Chats;

namespace DevHub.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "The 'DefaultConnection' SQL Server connection string is not configured.");

        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(connectionString));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IExternalAccountRepository, ExternalAccountRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IOrganizationRepository, OrganizationRepository>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IChatService, ChatService>();
        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IChatMessageCipher, AesChatMessageCipher>();

        var jwtSection = configuration.GetRequiredSection(JwtOptions.SectionName);
        var cookieSection = configuration.GetSection(AuthenticationCookieOptions.SectionName);

        services.AddOptions<AuthenticationCookieOptions>()
            .Bind(cookieSection);

        var cookieOptions = cookieSection.Get<AuthenticationCookieOptions>() ?? new();

        services
            .AddOptions<JwtOptions>()
            .Bind(jwtSection)
            .Validate(
                options =>
                    !string.IsNullOrWhiteSpace(options.Issuer) &&
                    !string.IsNullOrWhiteSpace(options.Audience) &&
                    !string.IsNullOrWhiteSpace(options.Key) &&
                    Encoding.UTF8.GetByteCount(options.Key) >= 32 &&
                    options.ExpirationMinutes > 0 &&
                    options.RefreshTokenExpirationDays > 0,
                "JWT settings require an issuer, audience, expiration, and a key of at least 32 bytes.")
            .ValidateOnStart();

        services
            .AddAuthentication(options =>
            {
                options.DefaultScheme = AuthenticationSchemes.CookieOrBearer;
                options.DefaultAuthenticateScheme = AuthenticationSchemes.CookieOrBearer;
                options.DefaultChallengeScheme = AuthenticationSchemes.CookieOrBearer;
                options.DefaultForbidScheme = AuthenticationSchemes.CookieOrBearer;
                options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.DefaultSignOutScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            })
            .AddPolicyScheme(
                AuthenticationSchemes.CookieOrBearer,
                "Cookie or Bearer",
                options =>
                {
                    options.ForwardDefaultSelector = context =>
                    {
                        var hasBearerHeader = context.Request.Headers.Authorization.ToString()
                            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase);
                        var hasHubAccessToken = context.Request.Path.StartsWithSegments("/hubs") &&
                            context.Request.Query.ContainsKey("access_token");

                        return hasBearerHeader || hasHubAccessToken
                            ? JwtBearerDefaults.AuthenticationScheme
                            : CookieAuthenticationDefaults.AuthenticationScheme;
                    };
                })
            .AddJwtBearer(options =>
            {
                var jwtOptions = jwtSection.Get<JwtOptions>()
                    ?? throw new InvalidOperationException("The 'Jwt' configuration section is invalid.");

                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwtOptions.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwtOptions.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Key)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        if (context.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = context.Request.Query["access_token"];
                        }

                        return Task.CompletedTask;
                    }
                };
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                var jwtOptions = jwtSection.Get<JwtOptions>()
                    ?? throw new InvalidOperationException("The 'Jwt' configuration section is invalid.");

                options.Cookie.Name = cookieOptions.AuthCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = cookieOptions.Secure
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                options.Cookie.SameSite = cookieOptions.GetSameSiteMode();
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromDays(jwtOptions.RefreshTokenExpirationDays);
                options.SlidingExpiration = true;

                options.Events.OnRedirectToLogin = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return Task.CompletedTask;
                };

                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddCookie(AuthenticationSchemes.ExternalCookie, options =>
            {
                options.Cookie.Name = "__Host-DevHub.External";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            })
            .AddGoogle(AuthenticationSchemes.Google, options =>
            {
                options.SignInScheme = AuthenticationSchemes.ExternalCookie;
                options.ClientId = GetOAuthCredential(
                    configuration,
                    "Authentication:Google:ClientId");
                options.ClientSecret = GetOAuthCredential(
                    configuration,
                    "Authentication:Google:ClientSecret");
                options.CallbackPath = "/signin-google";
                options.SaveTokens = false;
                options.UsePkce = true;
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.SameSite = SameSiteMode.None;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.ClaimActions.MapJsonKey(
                    ExternalAuthenticationConstants.AvatarUrlClaim,
                    "picture");
                options.ClaimActions.MapJsonKey(
                    ExternalAuthenticationConstants.EmailVerifiedClaim,
                    "email_verified");
                options.Events.OnRemoteFailure = context =>
                    HandleRemoteFailureAsync(context, configuration);
            })
            .AddOAuth(AuthenticationSchemes.GitHub, options =>
            {
                options.SignInScheme = AuthenticationSchemes.ExternalCookie;
                options.ClientId = GetOAuthCredential(
                    configuration,
                    "Authentication:GitHub:ClientId");
                options.ClientSecret = GetOAuthCredential(
                    configuration,
                    "Authentication:GitHub:ClientSecret");
                options.CallbackPath = "/signin-github";
                options.AuthorizationEndpoint = "https://github.com/login/oauth/authorize";
                options.TokenEndpoint = "https://github.com/login/oauth/access_token";
                options.UserInformationEndpoint = "https://api.github.com/user";
                options.Scope.Add("read:user");
                options.Scope.Add("user:email");
                options.SaveTokens = false;
                options.UsePkce = true;
                options.CorrelationCookie.HttpOnly = true;
                options.CorrelationCookie.SameSite = SameSiteMode.None;
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Events.OnCreatingTicket = PopulateGitHubClaimsAsync;
                options.Events.OnRemoteFailure = context =>
                    HandleRemoteFailureAsync(context, configuration);
            });

        services.AddAuthorization();

        return services;
    }

    private static string GetOAuthCredential(
        IConfiguration configuration,
        string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? "not-configured" : value;
    }

    private static async Task PopulateGitHubClaimsAsync(OAuthCreatingTicketContext context)
    {
        using var userRequest = new HttpRequestMessage(
            HttpMethod.Get,
            context.Options.UserInformationEndpoint);
        userRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            context.AccessToken);
        userRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        userRequest.Headers.UserAgent.ParseAdd("DevHub");

        using var userResponse = await context.Backchannel.SendAsync(
            userRequest,
            context.HttpContext.RequestAborted);
        userResponse.EnsureSuccessStatusCode();
        using var userDocument = JsonDocument.Parse(
            await userResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));
        var user = userDocument.RootElement;

        var providerUserId = user.GetProperty("id").GetInt64().ToString();
        var login = user.GetProperty("login").GetString();
        var displayName = user.TryGetProperty("name", out var nameElement)
            ? nameElement.GetString()
            : null;
        var avatarUrl = user.TryGetProperty("avatar_url", out var avatarElement)
            ? avatarElement.GetString()
            : null;
        var email = user.TryGetProperty("email", out var emailElement)
            ? emailElement.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(email))
        {
            email = await GetVerifiedGitHubEmailAsync(context);
        }

        var identity = (ClaimsIdentity)context.Principal!.Identity!;
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, providerUserId));
        identity.AddClaim(new Claim(ClaimTypes.Name, displayName ?? login ?? providerUserId));

        if (!string.IsNullOrWhiteSpace(login))
        {
            identity.AddClaim(new Claim(
                ExternalAuthenticationConstants.ProviderUsernameClaim,
                login));
        }

        if (!string.IsNullOrWhiteSpace(avatarUrl))
        {
            identity.AddClaim(new Claim(
                ExternalAuthenticationConstants.AvatarUrlClaim,
                avatarUrl));
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            identity.AddClaim(new Claim(ClaimTypes.Email, email));
            identity.AddClaim(new Claim(
                ExternalAuthenticationConstants.EmailVerifiedClaim,
                bool.TrueString));
        }
    }

    private static Task HandleRemoteFailureAsync(
        RemoteFailureContext context,
        IConfiguration configuration)
    {
        context.HandleResponse();
        var returnPath = context.Properties?.Items.TryGetValue(
            ExternalAuthenticationConstants.ReturnUrlItem,
            out var configuredReturnPath) == true
            ? configuredReturnPath
            : "/login";
        var allowedPaths = configuration
            .GetSection("Frontend:AllowedReturnPaths")
            .Get<string[]>() ?? ["/workshop", "/account/profile", "/account/account"];

        if (string.IsNullOrWhiteSpace(returnPath) ||
            !allowedPaths.Contains(returnPath, StringComparer.Ordinal))
        {
            returnPath = "/login";
        }

        var frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
        var destination = new Uri(new Uri(frontendBaseUrl), returnPath).ToString();
        context.Response.Redirect(QueryHelpers.AddQueryString(
            destination,
            "oauthError",
            "provider_rejected"));
        return Task.CompletedTask;
    }

    private static async Task<string?> GetVerifiedGitHubEmailAsync(
        OAuthCreatingTicketContext context)
    {
        using var emailRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.github.com/user/emails");
        emailRequest.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            context.AccessToken);
        emailRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        emailRequest.Headers.UserAgent.ParseAdd("DevHub");

        using var emailResponse = await context.Backchannel.SendAsync(
            emailRequest,
            context.HttpContext.RequestAborted);
        emailResponse.EnsureSuccessStatusCode();
        using var emailDocument = JsonDocument.Parse(
            await emailResponse.Content.ReadAsStringAsync(context.HttpContext.RequestAborted));

        return emailDocument.RootElement
            .EnumerateArray()
            .Where(email =>
                email.TryGetProperty("verified", out var verified) && verified.GetBoolean())
            .OrderByDescending(email =>
                email.TryGetProperty("primary", out var primary) && primary.GetBoolean())
            .Select(email => email.GetProperty("email").GetString())
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
