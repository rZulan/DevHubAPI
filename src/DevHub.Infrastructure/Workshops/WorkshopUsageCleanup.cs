using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevHub.Infrastructure.Workshops;

internal sealed class WorkshopUsageCleanup(IServiceScopeFactory scopes, TimeProvider clock, ILogger<WorkshopUsageCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    for (var batch = 0; batch < 10; batch++)
                        if (await db.Set<WorkshopUsageBucket>().Where(bucket => bucket.ExpiresAtUtc < clock.GetUtcNow()).Take(500).ExecuteDeleteAsync(stoppingToken) < 500) break;
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {break;}
                catch (Exception) {logger.LogWarning("Workshop quota ledger cleanup failed; it will retry on the next interval.");}
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
