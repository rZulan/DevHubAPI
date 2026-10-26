using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace DevHub.Api.BugReports;

public static class BugReportRateLimiting
{
    public static IServiceCollection AddBugReportRateLimiting(this IServiceCollection services) => services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.AddPolicy("public-bug-submit", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {PermitLimit = 10, Window = TimeSpan.FromHours(1), QueueLimit = 0, AutoReplenishment = true}));
        options.AddPolicy("public-bug-read", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions {PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true}));
        options.OnRejected = async (context, ct) =>
        {
            if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)) context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retry.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            await context.HttpContext.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails {Status = 429, Title = "Too many requests", Detail = "Please wait before trying again."}, ct);
        };
    });
}
