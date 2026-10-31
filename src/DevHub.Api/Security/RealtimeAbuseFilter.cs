using System.Threading.RateLimiting;
using Microsoft.AspNetCore.SignalR;

namespace DevHub.Api.Security;

/// <summary>HTTP limits do not apply to messages on an already established WebSocket.</summary>
public sealed class RealtimeAbuseFilter(Microsoft.Extensions.Options.IOptions<DevHub.Application.Workshops.WorkshopLimits>? configuration = null) : IHubFilter, IDisposable
{
    private readonly PartitionedRateLimiter<HubInvocationContext> _messages = PartitionedRateLimiter.Create<HubInvocationContext, string>(context =>
    {
        var subject = context.Context.UserIdentifier ?? context.Context.ConnectionId;
        var chat = context.HubMethodName == "SendMessage";
        var selection = context.HubMethodName == "SetIdeasSelection";
        var category = chat ? "chat" : selection ? "selection" : "membership";
        return RateLimitPartition.GetTokenBucketLimiter($"{subject}:{category}", _ => new()
        {
            TokenLimit = chat ? (configuration?.Value.ChatBurst ?? 5) : selection ? 20 : 10,
            TokensPerPeriod = chat ? 1 : selection ? 20 : 10,
            ReplenishmentPeriod = chat ? TimeSpan.FromMinutes(1) / (configuration?.Value.ChatMessagesPerMinute ?? 30) : TimeSpan.FromSeconds(selection ? 1 : 10),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
    private readonly PartitionedRateLimiter<HubLifetimeContext> _connections = PartitionedRateLimiter.Create<HubLifetimeContext, string>(context =>
        RateLimitPartition.GetConcurrencyLimiter(context.Context.UserIdentifier ?? context.Context.ConnectionId, _ => new() {PermitLimit = 20, QueueLimit = 0}));
    private readonly PartitionedRateLimiter<HubInvocationContext> _userWork = PartitionedRateLimiter.Create<HubInvocationContext, string>(context =>
        RateLimitPartition.GetConcurrencyLimiter(context.Context.UserIdentifier ?? context.Context.ConnectionId, _ => new() {PermitLimit = 4, QueueLimit = 0}));
    private readonly ConcurrencyLimiter _serverWork = new(new() {PermitLimit = 16, QueueLimit = 0});
    private static readonly object ConnectionLeaseKey = new();

    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        using var lease = _messages.AttemptAcquire(context);
        if (!lease.IsAcquired) throw new HubException("Too many realtime updates. Please wait before trying again.");
        using var userWork = _userWork.AttemptAcquire(context);
        if (!userWork.IsAcquired) throw new HubException("Too many pending realtime updates. Please wait before trying again.");
        using var serverWork = _serverWork.AttemptAcquire();
        if (!serverWork.IsAcquired) throw new HubException("Realtime capacity is busy. Please wait before trying again.");
        return await next(context);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        var lease = _connections.AttemptAcquire(context);
        if (!lease.IsAcquired)
        {
            lease.Dispose();
            context.Context.Abort();
            throw new HubException("Too many active realtime connections.");
        }
        context.Context.Items[ConnectionLeaseKey] = lease;
        try {await next(context);}
        catch {Release(context); throw;}
    }

    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        try {await next(context, exception);}
        finally {Release(context);}
    }

    private static void Release(HubLifetimeContext context)
    {
        if (context.Context.Items.Remove(ConnectionLeaseKey, out var value) && value is RateLimitLease lease) lease.Dispose();
    }

    public void Dispose() {_messages.Dispose(); _connections.Dispose(); _userWork.Dispose(); _serverWork.Dispose();}
}
