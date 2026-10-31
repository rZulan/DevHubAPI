namespace DevHub.Api.Security;

/// <summary>CORS alone does not reject credentialed WebSocket handshakes or simple cross-origin writes.</summary>
public sealed class BrowserOriginPolicy
{
    private readonly HashSet<string> _origins;

    public BrowserOriginPolicy(IConfiguration configuration)
    {
        var origins = configuration.GetSection("Frontend:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"];
        _origins = origins.Select(Normalize).Where(origin => origin is not null).Select(origin => origin!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public bool IsAllowed(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/api") && !request.Path.StartsWithSegments("/hubs")) return true;
        if (!RequestSecurity.IsWrite(request.Method) && !request.Path.StartsWithSegments("/hubs")) return true;
        if (request.Headers.TryGetValue("Origin", out var origin))
            return origin.Count == 1 && Normalize(origin[0]) is { } normalized && _origins.Contains(normalized);
        // Non-browser API clients can omit Origin. Modern cross-site browser requests cannot
        // bypass the check by omitting it: their Fetch Metadata is independently rejected.
        return !string.Equals(request.Headers["Sec-Fetch-Site"], "cross-site", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Normalize(string? value) => Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo)
        && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        ? uri.GetLeftPart(UriPartial.Authority) : null;
}

public sealed class BrowserRequestSecurityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, BrowserOriginPolicy origins)
    {
        if (context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.OnStarting(() =>
            {
                context.Response.Headers.CacheControl = "no-store";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                context.Response.Headers["Referrer-Policy"] = "no-referrer";
                return Task.CompletedTask;
            });
        }
        if (!origins.IsAllowed(context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new Microsoft.AspNetCore.Mvc.ProblemDetails
                {Status = 403, Title = "Origin not allowed", Detail = "This browser origin cannot perform this request."});
            return;
        }
        await next(context);
    }
}
