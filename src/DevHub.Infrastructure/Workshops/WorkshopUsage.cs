using System.Data;
using DevHub.Application.Common;
using DevHub.Application.Workshops;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Workshops;

// A separate context commits reservations independently of callers' tracked entities.
// Serializable range locks and a stable lock order also protect simultaneous API instances.
internal sealed class WorkshopUsage(DbContextOptions<ApplicationDbContext> options, TimeProvider clock) : IWorkshopUsage
{
    public async Task<int> Take(string key, int limit, int requested, TimeSpan window, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var start = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / (long)window.TotalSeconds * (long)window.TotalSeconds);
        await using var probe = new ApplicationDbContext(options);
        return await probe.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = new ApplicationDbContext(options);
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var bucket = await db.Set<WorkshopUsageBucket>().FromSql($"SELECT * FROM [WorkshopUsageBuckets] WITH (UPDLOCK, HOLDLOCK) WHERE [Key] = {key} AND [WindowStartUtc] = {start}").SingleOrDefaultAsync(ct);
            var granted = Math.Min(requested, Math.Max(0, limit - (bucket?.Used ?? 0)));
            if (granted == 0) return 0;
            if (bucket is null) {bucket = new() {Key = key, WindowStartUtc = start, ExpiresAtUtc = start + window + TimeSpan.FromDays(1)}; db.Add(bucket);}
            bucket.Used += granted;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return granted;
        });
    }

    public async Task<Error?> Reserve(IReadOnlyList<WorkshopCharge> charges, CancellationToken ct)
    {
        if (charges.Count == 0) return null;
        var now = clock.GetUtcNow();
        await using var probe = new ApplicationDbContext(options);
        return await probe.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var db = new ApplicationDbContext(options);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            foreach (var charge in charges.OrderBy(c => c.Key, StringComparer.Ordinal))
            {
                if (charge.Units < 1 || charge.Limit < 1 || charge.Window <= TimeSpan.Zero || charge.Key.Length > 180)
                    throw new ArgumentOutOfRangeException(nameof(charges));
                var start = DateTimeOffset.FromUnixTimeSeconds(now.ToUnixTimeSeconds() / (long)charge.Window.TotalSeconds * (long)charge.Window.TotalSeconds);
                // UPDLOCK avoids lock-conversion deadlocks on the shared counter row/range.
                var bucket = await db.Set<WorkshopUsageBucket>().FromSql($"SELECT * FROM [WorkshopUsageBuckets] WITH (UPDLOCK, HOLDLOCK) WHERE [Key] = {charge.Key} AND [WindowStartUtc] = {start}").SingleOrDefaultAsync(ct);
                if ((long)(bucket?.Used ?? 0) + charge.Units > charge.Limit)
                    return new Error("Workshop.RateLimit", "This workshop action has reached its limit. Your draft is preserved; try again after the cooldown.", ErrorType.Throttled,
                        RetryAfterSeconds: Math.Max(1, (int)Math.Ceiling((start + charge.Window - now).TotalSeconds)));
                if (bucket is null)
                {
                    bucket = new() {Key = charge.Key, WindowStartUtc = start, ExpiresAtUtc = start + charge.Window + TimeSpan.FromDays(1)};
                    db.Add(bucket);
                }
                bucket.Used += charge.Units;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return null;
        });
    }
}
