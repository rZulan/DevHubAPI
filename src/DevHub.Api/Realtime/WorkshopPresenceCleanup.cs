using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Realtime;

public sealed class WorkshopPresenceCleanup(
    WorkshopPresenceTracker tracker,
    IHubContext<WorkshopHub> hub,
    ILogger<WorkshopPresenceCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var result in tracker.ExpireHeartbeats().Where(result => result.IsNowOffline))
            {
                try
                {
                    await hub.Clients.Group(WorkshopHub.GetGroupName(result.OrganizationId)).SendAsync(
                        "MemberOffline", result.UserId.ToString(), result.OrganizationId.ToString(), stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(exception, "Unable to broadcast expired workshop presence");
                }
            }
        }
    }
}
