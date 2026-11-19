using System.Data;
using DevHub.Application.Common;
using DevHub.Application.Connections;
using DevHub.Domain.Notifications;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DevHub.Infrastructure.Connections;

internal sealed class ConnectionWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ConnectionWorker> logger) : BackgroundService
{
    private const int MaxAttempts = 4;
    private const int MaxChainDepth = 8;
    private const int MaxChainRuns = 50;
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var nextCleanup = clock.GetUtcNow();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await scope.ServiceProvider.GetRequiredService<ConnectionCalendarSchedule>().PublishDue(stoppingToken);
                var evt = await Claim(db, stoppingToken);
                if (evt is not null)
                {
                    await Dispatch(db, evt, stoppingToken);
                    continue;
                }

                if (clock.GetUtcNow() >= nextCleanup)
                {
                    await Cleanup(db, stoppingToken);
                    nextCleanup = clock.GetUtcNow().AddHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogError(exception, "Project Connections could not process its pending events.");
                await Task.Delay(TimeSpan.FromSeconds(10), clock, stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(2), clock, stoppingToken);
        }
    }

    private async Task<QueuedConnectionEvent?> Claim(ApplicationDbContext db, CancellationToken ct)
    {
        var events = db.Set<QueuedConnectionEvent>();
        var now = clock.GetUtcNow();
        var candidate = await events.AsNoTracking().Where(evt => evt.FinishedAtUtc == null
                && (evt.LeasedUntilUtc == null || evt.LeasedUntilUtc <= now)
                && !events.Any(prior => prior.ProjectId == evt.ProjectId && prior.Position < evt.Position && prior.FinishedAtUtc == null))
            .OrderBy(evt => evt.Position).FirstOrDefaultAsync(ct);
        if (candidate is null) return null;

        var leaseId = Guid.NewGuid();
        var until = now.Add(LeaseDuration);
        var changed = await events.Where(evt => evt.Position == candidate.Position && evt.FinishedAtUtc == null
                && (evt.LeasedUntilUtc == null || evt.LeasedUntilUtc <= now))
            .ExecuteUpdateAsync(set => set.SetProperty(evt => evt.LeaseId, leaseId).SetProperty(evt => evt.LeasedUntilUtc, until)
                .SetProperty(evt => evt.Attempts, evt => evt.Attempts + 1), ct);
        if (changed == 0) return null;
        candidate.LeaseId = leaseId;
        candidate.LeasedUntilUtc = until;
        candidate.Attempts++;
        return candidate;
    }

    private async Task Dispatch(ApplicationDbContext db, QueuedConnectionEvent evt, CancellationToken ct)
    {
        try
        {
            var runs = await Schedule(db, evt, ct);
            foreach (var id in runs)
            {
                while (await ProcessStep(db, evt, id, ct)) { }
            }
            await db.Set<QueuedConnectionEvent>().Where(row => row.Position == evt.Position && row.LeaseId == evt.LeaseId)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.FinishedAtUtc, clock.GetUtcNow())
                    .SetProperty(row => row.LeaseId, (Guid?)null).SetProperty(row => row.LeasedUntilUtc, (DateTimeOffset?)null), ct);
        }
        catch (RetryConnectionStep exception)
        {
            var delay = Math.Max(2 << evt.Attempts, exception.DelaySeconds);
            await db.Set<QueuedConnectionEvent>().Where(row => row.Position == evt.Position && row.LeaseId == evt.LeaseId)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.LeaseId, (Guid?)null)
                    .SetProperty(row => row.LeasedUntilUtc, clock.GetUtcNow().AddSeconds(delay)), ct);
        }
    }

    private async Task<Guid[]> Schedule(ApplicationDbContext db, QueuedConnectionEvent evt, CancellationToken ct)
    {
        var current = await db.Set<StoredConnectionRun>().AsNoTracking().Where(run => run.EventPosition == evt.Position).ToArrayAsync(ct);
        var definitions = await db.Set<StoredConnection>().AsNoTracking().Where(connection => connection.ProjectId == evt.ProjectId && !connection.Deleted)
            .OrderBy(connection => connection.Id).ToArrayAsync(ct);
        var draft = evt.Draft;
        var chain = evt.Chain;
        var rootPosition = evt.RootPosition ?? evt.Position;
        var total = await db.Set<StoredConnectionRun>().CountAsync(run => run.ProjectId == evt.ProjectId && run.RootPosition == rootPosition, ct);
        var pending = current.Where(run => run.Status == "pending").Select(run => run.Id).ToList();
        foreach (var connection in definitions)
        {
            var versions = connection.Rules;
            var consumed = versions.Where(version => version.ActiveThrough is null && version.Rule.RunOnce && version.TriggeredRunId is not null)
                .Select(version => (version.Rule.Id, version.ActivationId)).ToHashSet();
            foreach (var version in versions)
            {
                var rule = version.Rule;
                if (version.ActiveAfter >= evt.Position || (version.ActiveThrough is { } end && end < evt.Position) || !rule.Enabled || !Matches(draft, rule)
                    || (ConnectionCalendarSchedule.IsScheduled(draft.Kind) && evt.OccurredAtUtc < version.ActiveAtUtc)
                    || (chain.Length > 0 && !rule.IncludeAutomation)
                    || consumed.Contains((rule.Id, version.ActivationId))
                    || current.Any(run => run.ConnectionId == connection.Id && run.RuleId == rule.Id)) continue;

                var limit = chain.Length >= MaxChainDepth || total >= MaxChainRuns || chain.Contains(rule.Id);
                var run = new StoredConnectionRun
                {
                    Id = Guid.NewGuid(), ProjectId = evt.ProjectId, ConnectionId = connection.Id, ConnectionRevision = version.ConnectionRevision,
                    OwnerUserId = version.OwnerUserId, RuleId = rule.Id, RuleJson = ConnectionStorage.Write(version),
                    EventPosition = evt.Position, RootPosition = rootPosition, Event = draft.Kind, SourceJson = ConnectionStorage.Write(draft.Node),
                    OccurredAtUtc = evt.OccurredAtUtc, Status = limit ? "skipped" : "pending",
                    Detail = limit ? "The automation chain reached its limit or returned to this rule." : null
                };
                db.Set<StoredConnectionRun>().Add(run);
                if (!limit) pending.Add(run.Id);
                total++;
            }
        }
        await db.SaveChangesAsync(ct);
        return pending.ToArray();
    }

    internal static bool Matches(ConnectionEventDraft evt, ConnectionRule rule) =>
        evt.Kind == rule.Event && (evt.Node == rule.Source || evt.Ancestors.Contains(rule.Source))
        && rule.EventArguments.All(argument => string.IsNullOrWhiteSpace(argument.Value)
            || (evt.Fields.TryGetValue(argument.Key, out var value) && MatchesField(argument.Key, value, argument.Value.Trim())));

    private static bool MatchesField(string key, string actual, string expected) => key == "minutesBefore"
        ? ConnectionCalendarSchedule.TryOffset(actual, out var minutes) && ConnectionCalendarSchedule.TryOffset(expected, out var requested) && minutes == requested
        : string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);

    private async Task<bool> ProcessStep(ApplicationDbContext coordinator, QueuedConnectionEvent evt, Guid runId, CancellationToken ct)
    {
        var strategy = coordinator.Database.CreateExecutionStrategy();
        StepProgress progress;
        try
        {
            progress = await strategy.ExecuteAsync(_ => ApplyStep(evt, runId, ct), ct);
        }
        catch (FailedConnectionStep failure)
        {
            await RecordFailure(coordinator, evt, runId, failure.Detail, ct);
            return false;
        }
        catch (Exception exception) when (exception is not RetryConnectionStep && !ct.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Project Connections action failed for run {RunId}.", runId);
            if (evt.Attempts < MaxAttempts) throw new RetryConnectionStep(0);
            await RecordFailure(coordinator, evt, runId, "The action could not finish after several attempts.", ct);
            return false;
        }

        if (progress.Owner is { } owner && progress.Target is { } target)
        {
            using var scope = scopes.CreateScope();
            try { await scope.ServiceProvider.GetRequiredService<IConnectionChangeNotifier>().Changed(owner, target, ct); }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Could not publish the realtime change for a committed automation action.");
            }
        }
        return progress.More;
    }

    private async Task<StepProgress> ApplyStep(QueuedConnectionEvent evt, Guid runId, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var tools = services.GetRequiredService<IConnectionTools>();
        var access = services.GetRequiredService<ConnectionAuthorization>();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        if (!await LeaseValid(db, evt, ct)) return new(false);

        var run = await db.Set<StoredConnectionRun>().SingleAsync(row => row.Id == runId, ct);
        if (run.Status != "pending") return new(false);
        var version = run.Definition;
        var rule = version.Rule;
        var steps = run.Steps.ToList();
        var next = rule.Actions[steps.Count];
        var owner = new ConnectionScope(evt.OrganizationId, evt.ProjectId, run.OwnerUserId);
        var definition = await db.Set<StoredConnection>().SingleOrDefaultAsync(row => row.Id == run.ConnectionId, ct);
        var versions = definition?.Rules ?? [];
        var active = versions.FirstOrDefault(value => value.Rule.Id == run.RuleId && value.ActiveThrough is null);
        string? skipped = null;
        if (definition is null || definition.Deleted || definition.OwnerUserId != run.OwnerUserId || active is null || !active.Rule.Enabled
            || active.ActivationId != version.ActivationId)
            skipped = "The rule was paused or its connection changed ownership before this action.";
        else if (rule.RunOnce && active.TriggeredRunId is { } triggeredRunId && triggeredRunId != run.Id)
            skipped = "This run-once rule has already triggered.";
        else if (!await access.CanView(owner, ct) || !await access.HasManagePermission(owner, ct) || !await tools.CanManage(owner, next.Target.Kind, ct))
            skipped = "The rule owner no longer has permission to apply this action.";
        else if (steps.Count == 0)
        {
            var calendar = services.GetRequiredService<ConnectionCalendarSchedule>();
            skipped = await calendar.IsCurrent(evt.ProjectId, evt.Draft, ct)
                ? await CheckConditions(tools, owner, rule, ct)
                : "The Calendar entry changed after this occurrence was queued.";
        }

        if (skipped is not null)
        {
            Complete(run, steps, rule.Actions.Skip(steps.Count), tools, steps.Count == 0 ? "skipped" : "partial", skipped);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(false);
        }

        // Commit admission separately so action failures and retries retain the same trigger and notification.
        if (steps.Count == 0 && version.TriggeredRunId is null)
        {
            var now = clock.GetUtcNow();
            if (rule.RunOnce)
            {
                definition!.RulesJson = ConnectionStorage.Write(versions.Select(value =>
                    value == active ? value with { TriggeredRunId = run.Id } : value));
                definition.Revision++;
                definition.UpdatedAtUtc = now;
            }
            run.RuleJson = ConnectionStorage.Write(version with { TriggeredRunId = run.Id });
            db.UserNotifications.Add(new UserNotification(owner.OrganizationId, owner.UserId, owner.UserId,
                UserNotification.ConnectionTriggered, rule.Title, run.ConnectionId, run.Id, now, owner.ProjectId));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(true);
        }

        var execution = services.GetRequiredService<ConnectionExecutionContext>();
        execution.Chain = [.. evt.Chain, rule.Id];
        execution.RootPosition = run.RootPosition;
        var action = ConnectionBindings.Resolve(next, steps, evt.Draft);
        if (!action.IsSuccess) throw new FailedConnectionStep(action.Error!.Description);
        var resolved = action.Value!;
        if (next.ArgumentBindings is { Count: > 0 } || next.TargetFromStepId is not null)
        {
            var actualTarget = await tools.ResolveNode(owner, resolved.Target, ct);
            var choices = tools.Catalog.Single(entry => entry.NodeKind == resolved.Target.Kind).ActionChoices;
            if (actualTarget is null || !ProjectConnections.ValidateChoice(resolved.Kind, resolved.Arguments, choices, actualTarget))
                throw new FailedConnectionStep("The action’s resolved item or field value is unavailable.");
        }
        var outcome = await tools.Execute(owner, resolved, ct);
        if (!outcome.IsSuccess)
        {
            var error = outcome.Error!;
            if (evt.Attempts < MaxAttempts && error.Type is ErrorType.Conflict or ErrorType.Throttled)
                throw new RetryConnectionStep(error.RetryAfterSeconds ?? 0);
            throw new FailedConnectionStep(SafeDetail(error.Description) ?? "The action failed.");
        }

        var result = outcome.Value!;
        steps.Add(new(new(next.Id, ActionLabel(tools, next), "succeeded", SafeDetail(result.Detail)), result.CreatedNode));
        var more = steps.Count < rule.Actions.Count;
        run.Status = more ? "pending" : "succeeded";
        run.StepsJson = ConnectionStorage.Write(steps);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(more, owner, resolved.Target);
    }

    private async Task RecordFailure(ApplicationDbContext coordinator, QueuedConnectionEvent evt, Guid runId, string detail, CancellationToken ct)
    {
        await coordinator.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            using var scope = scopes.CreateScope();
            var services = scope.ServiceProvider;
            var db = services.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            if (!await LeaseValid(db, evt, ct)) return;
            var run = await db.Set<StoredConnectionRun>().SingleAsync(row => row.Id == runId, ct);
            if (run.Status != "pending") return;
            var tools = services.GetRequiredService<IConnectionTools>();
            var rule = run.Rule;
            var steps = run.Steps.ToList();
            var next = rule.Actions[steps.Count];
            steps.Add(new(new(next.Id, ActionLabel(tools, next), "failed", detail)));
            Complete(run, steps, rule.Actions.Skip(steps.Count), tools, steps.Count == 1 ? "failed" : "partial", detail);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
    }

    private static Task<bool> LeaseValid(ApplicationDbContext db, QueuedConnectionEvent evt, CancellationToken ct) =>
        db.Set<QueuedConnectionEvent>().AnyAsync(row => row.Position == evt.Position && row.LeaseId == evt.LeaseId && row.FinishedAtUtc == null, ct);

    private static async Task<string?> CheckConditions(IConnectionTools tools, ConnectionScope owner, ConnectionRule rule, CancellationToken ct)
    {
        var evaluation = await ConnectionConditionEvaluation.Check(tools, owner, rule, ct);
        return evaluation.Status switch
        {
            "ready" => null,
            "conditions-unmet" => "The configured conditions did not match.",
            _ => "A configured condition could not be checked."
        };
    }

    private static void Complete(StoredConnectionRun run, List<StoredStep> steps, IEnumerable<ConnectionAction> remaining, IConnectionTools tools,
        string status, string? detail)
    {
        steps.AddRange(remaining.Select(action => new StoredStep(new(action.Id, ActionLabel(tools, action), "skipped"))));
        run.StepsJson = ConnectionStorage.Write(steps);
        run.Status = status;
        run.Detail = detail;
    }

    private static string ActionLabel(IConnectionTools tools, ConnectionAction action) =>
        tools.Catalog.FirstOrDefault(entry => entry.NodeKind == action.Target.Kind)?.ActionChoices.FirstOrDefault(choice => choice.Kind == action.Kind)?.Label ?? action.Kind;

    private static string? SafeDetail(string? value) => value?.Length > 400 ? value[..400] : value;

    private async Task Cleanup(ApplicationDbContext db, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.Set<QueuedConnectionEvent>().Where(evt => evt.FinishedAtUtc < now.AddDays(-14)).Take(1000).ExecuteDeleteAsync(ct);
        await db.Set<StoredConnectionRun>().Where(run => run.Status != "pending" && run.OccurredAtUtc < now.AddDays(-30)).Take(1000).ExecuteDeleteAsync(ct);
    }

    private sealed record StepProgress(bool More, ConnectionScope? Owner = null, ConnectionNodeRef? Target = null);
    private sealed class FailedConnectionStep(string detail) : Exception(detail)
    {
        public string Detail { get; } = detail;
    }
    private sealed class RetryConnectionStep(int delaySeconds) : Exception
    {
        public int DelaySeconds { get; } = delaySeconds;
    }
}
