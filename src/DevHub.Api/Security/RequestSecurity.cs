using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DevHub.Api.Security;

public sealed class RequestSecurityLimits
{
    public int RequestsPerMinute { get; set; } = 300;
    public int WritesPerMinute { get; set; } = 120;
    public int LoginsPerMinute { get; set; } = 10;
    public int RegistrationsPerHour { get; set; } = 5;
    public int RefreshesPerMinute { get; set; } = 30;
    public int ConcurrentRequests { get; set; } = 32;
    public int ConcurrentUploads { get; set; } = 2;
}

public static class RequestSecurity
{
    public static IServiceCollection AddRequestSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        var limits = configuration.GetSection("Security:Limits").Get<RequestSecurityLimits>() ?? new();
        if (new[] { limits.RequestsPerMinute, limits.WritesPerMinute, limits.LoginsPerMinute,
                limits.RegistrationsPerHour, limits.RefreshesPerMinute, limits.ConcurrentRequests,
                limits.ConcurrentUploads }.Any(value => value is < 1 or > 100_000))
            throw new InvalidOperationException("Security limits must be between 1 and 100000.");

        services.AddSingleton<BrowserOriginPolicy>();
        services.AddSingleton<RealtimeAbuseFilter>();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    RateLimitPartition.GetFixedWindowLimiter(ClientIp(context), _ => Window(limits.RequestsPerMinute, TimeSpan.FromMinutes(1)))),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    IsWrite(context.Request.Method)
                        ? RateLimitPartition.GetFixedWindowLimiter(Subject(context), _ => Window(limits.WritesPerMinute, TimeSpan.FromMinutes(1)))
                        : RateLimitPartition.GetNoLimiter("read")),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    context.WebSockets.IsWebSocketRequest || context.Request.Path.StartsWithSegments("/hubs")
                        ? RateLimitPartition.GetNoLimiter("realtime")
                        : RateLimitPartition.GetConcurrencyLimiter("requests", _ => new() {PermitLimit = limits.ConcurrentRequests, QueueLimit = 0})),
                PartitionedRateLimiter.Create<HttpContext, string>(context =>
                    HttpMethods.IsPost(context.Request.Method) && context.Request.HasFormContentType
                        ? RateLimitPartition.GetConcurrencyLimiter("uploads", _ => new() {PermitLimit = limits.ConcurrentUploads, QueueLimit = 0})
                        : RateLimitPartition.GetNoLimiter("other")));
            options.AddPolicy("auth-login", context => RateLimitPartition.GetFixedWindowLimiter(ClientIp(context), _ => Window(limits.LoginsPerMinute, TimeSpan.FromMinutes(1))));
            options.AddPolicy("auth-register", context => RateLimitPartition.GetFixedWindowLimiter(ClientIp(context), _ => Window(limits.RegistrationsPerHour, TimeSpan.FromHours(1))));
            options.AddPolicy("auth-refresh", context => RateLimitPartition.GetFixedWindowLimiter(ClientIp(context), _ => Window(limits.RefreshesPerMinute, TimeSpan.FromMinutes(1))));
            options.AddPolicy("resource-create", context => RateLimitPartition.GetFixedWindowLimiter(Subject(context), _ => Window(10, TimeSpan.FromMinutes(1))));
            options.AddPolicy("invite-action", context => RateLimitPartition.GetFixedWindowLimiter(Subject(context), _ => Window(5, TimeSpan.FromMinutes(1))));
            var workshop = configuration.GetSection(DevHub.Application.Workshops.WorkshopLimits.Section).Get<DevHub.Application.Workshops.WorkshopLimits>() ?? new();
            options.AddPolicy("workshop-save", context => RateLimitPartition.GetSlidingWindowLimiter(
                $"{Subject(context)}:{context.Request.RouteValues["projectId"]}:{context.Request.RouteValues["tool"] ?? "ideas"}",
                _ => new() {PermitLimit = workshop.SavesPerMinute, Window = TimeSpan.FromMinutes(1), SegmentsPerWindow = 6, QueueLimit = 0}));
            options.OnRejected = async (context, ct) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry))
                    context.HttpContext.Response.Headers.RetryAfter = Math.Max(1, Math.Ceiling(retry.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                await context.HttpContext.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = 429, Title = "Too many requests", Detail = "Please wait before trying again."
                }, ct);
            };
        });
        return services;
    }

    internal static string ClientIp(HttpContext context) => context.Connection.RemoteIpAddress?.MapToIPv6().ToString() ?? "unknown";
    internal static string Subject(HttpContext context) => context.User.FindFirst("sub")?.Value is {Length: > 0} subject
        && context.User.Identity?.IsAuthenticated == true ? $"user:{subject}" : $"ip:{ClientIp(context)}";
    internal static bool IsWrite(string method) => !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
    public static int ExceptionStatusCode(Exception error) => error is BadHttpRequestException {StatusCode: >= 400 and < 500} bad
        ? bad.StatusCode : StatusCodes.Status500InternalServerError;
    private static FixedWindowRateLimiterOptions Window(int permits, TimeSpan window) => new()
        {PermitLimit = permits, Window = window, QueueLimit = 0, AutoReplenishment = true};
}
