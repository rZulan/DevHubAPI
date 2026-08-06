using System.Text;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ProjectRoadMapper.Application.Abstractions.Authentication;
using ProjectRoadMapper.Application.Abstractions.Persistence;
using ProjectRoadMapper.Infrastructure.Authentication;
using ProjectRoadMapper.Infrastructure.Persistence;
using ProjectRoadMapper.Infrastructure.Persistence.Repositories;

namespace ProjectRoadMapper.Infrastructure;

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
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IUnitOfWork>(serviceProvider =>
            serviceProvider.GetRequiredService<ApplicationDbContext>());

        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IPasswordHasher, PasswordHasherAdapter>();
        services.AddSingleton<IJwtTokenGenerator, JwtTokenGenerator>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();

        var jwtSection = configuration.GetRequiredSection(JwtOptions.SectionName);

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
                        context.Request.Headers.Authorization.ToString()
                            .StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                            ? JwtBearerDefaults.AuthenticationScheme
                            : CookieAuthenticationDefaults.AuthenticationScheme;
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
            })
            .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
            {
                var jwtOptions = jwtSection.Get<JwtOptions>()
                    ?? throw new InvalidOperationException("The 'Jwt' configuration section is invalid.");

                options.Cookie.Name = "__Host-ProjectRoadMapper.Auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
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
            });

        services.AddAuthorization();

        return services;
    }
}
