using System.Data;
using System.Globalization;
using System.Text.Json;
using DevHub.Application.Connections;
using DevHub.Domain.Projects;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DevHub.Infrastructure.Connections;

internal sealed class ScheduledCalendarEvent
{
    public Guid ProjectId { get; set; }
    public Guid OrganizationId { get; set; }
    public string EntryId { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string EntryKind { get; set; } = string.Empty;
    public DateTimeOffset DueAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}

internal sealed class ScheduledCalendarEventConfiguration : IEntityTypeConfiguration<ScheduledCalendarEvent>
{
    public void Configure(EntityTypeBuilder<ScheduledCalendarEvent> entity)
    {
        entity.ToTable("ProjectConnectionCalendarSchedules");
        entity.HasKey(occurrence => new { occurrence.ProjectId, occurrence.EntryId, occurrence.Event });
        entity.Property(occurrence => occurrence.EntryId).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2");
        entity.Property(occurrence => occurrence.Event).HasMaxLength(40);
        entity.Property(occurrence => occurrence.EntryKind).HasMaxLength(20);
        entity.HasIndex(occurrence => new { occurrence.PublishedAtUtc, occurrence.DueAtUtc });
        entity.HasOne<Project>().WithMany().HasForeignKey(occurrence => occurrence.ProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ConnectionCalendarSchedule(ApplicationDbContext db, IConnectionEvents events, TimeProvider clock) : IConnectionCalendarSchedule
{
    public const string Started = "calendar-started";
    public const string Ended = "calendar-ended";
    public const string StartOffset = "calendar-start-offset";

    internal static bool IsScheduled(string kind) => kind is Started or Ended or StartOffset;

    internal static bool TryOffset(string? value, out int minutes) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out minutes) && minutes > 0
        && value == minutes.ToString(CultureInfo.InvariantCulture);

    private static string OffsetKey(int minutes) => $"{StartOffset}:{minutes.ToString(CultureInfo.InvariantCulture)}";

    internal Task<bool> IsCurrent(Guid projectId, ConnectionEventDraft draft, CancellationToken ct)
    {
        if (!IsScheduled(draft.Kind)) return Task.FromResult(true);
        if (draft.Node.Id is not { } entry || draft.OccurredAtUtc is not { } due) return Task.FromResult(false);
        var kind = draft.Fields.GetValueOrDefault("kind");
        var key = draft.Kind;
        if (key == StartOffset)
        {
            if (!TryOffset(draft.Fields.GetValueOrDefault("minutesBefore"), out var minutes)) return Task.FromResult(false);
            key = OffsetKey(minutes);
        }
        return db.Set<ScheduledCalendarEvent>().AnyAsync(occurrence => occurrence.ProjectId == projectId && occurrence.EntryId == entry
            && occurrence.Event == key && occurrence.DueAtUtc == due && occurrence.PublishedAtUtc != null && occurrence.EntryKind == kind, ct);
    }

    public async Task Sync(Guid organizationId, Guid projectId, JsonElement document, CancellationToken ct)
    {
        if (document.ValueKind != JsonValueKind.Object || !document.TryGetProperty("events", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return;

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(document.GetProperty("timeZone").GetString()!); }
        catch (TimeZoneNotFoundException) { return; }
        catch (InvalidTimeZoneException) { return; }

        var current = (await db.Set<ScheduledCalendarEvent>().Where(occurrence => occurrence.ProjectId == projectId).ToArrayAsync(ct))
            .ToDictionary(occurrence => (occurrence.EntryId, occurrence.Event));
        var offsets = await OffsetRules(projectId, ct);
        var now = clock.GetUtcNow();
        foreach (var entry in entries.EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString()!;
            var kind = entry.GetProperty("kind").GetString()!;
            var allDay = entry.GetProperty("allDay").GetBoolean();
            var start = Date(entry, "startDate", "startTime", allDay, false, zone);
            Update(Started, start);
            Update(Ended, Date(entry, "endDate", "endTime", allDay, true, zone));
            if (start is { } instant)
                foreach (var minutes in offsets.Where(offset => (offset.Entry is null || offset.Entry == id)
                        && (string.IsNullOrEmpty(offset.Kind) || offset.Kind == kind)).Select(offset => offset.Minutes).Distinct())
                    if (instant > DateTimeOffset.MinValue.AddMinutes(minutes)) Update(OffsetKey(minutes), instant.AddMinutes(-minutes));

            void Update(string eventName, DateTimeOffset? due)
            {
                if (due is null) return;
                if (current.Remove((id, eventName), out var existing))
                {
                    if (existing.DueAtUtc != due)
                    {
                        existing.DueAtUtc = due.Value;
                        existing.PublishedAtUtc = due > now ? null : now;
                    }
                    existing.EntryKind = kind;
                }
                else if (due > now)
                {
                    db.Add(new ScheduledCalendarEvent
                    {
                        OrganizationId = organizationId, ProjectId = projectId, EntryId = id,
                        EntryKind = kind, Event = eventName, DueAtUtc = due.Value
                    });
                }
            }
        }
        db.RemoveRange(current.Values);
    }

    private async Task<List<(string? Entry, string? Kind, int Minutes)>> OffsetRules(Guid projectId, CancellationToken ct)
    {
        var rules = await db.Set<StoredConnection>().AsNoTracking().Where(connection => connection.ProjectId == projectId && !connection.Deleted)
            .ToDictionaryAsync(connection => connection.Id, connection => connection.RulesJson, ct);
        // A connection can enable or remove a timer in the same commit as the schedule update.
        foreach (var entry in db.ChangeTracker.Entries<StoredConnection>().Where(entry => entry.Entity.ProjectId == projectId
                     && entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            if (entry.Entity.Deleted || entry.State == EntityState.Deleted) rules.Remove(entry.Entity.Id);
            else rules[entry.Entity.Id] = entry.Entity.RulesJson;
        }
        List<(string? Entry, string? Kind, int Minutes)> offsets = [];
        foreach (var json in rules.Values)
            foreach (var stored in ConnectionStorage.Read<StoredRule[]>(json))
            {
                var rule = stored.Rule;
                if (stored.ActiveThrough is null && rule.Enabled && rule.Event == StartOffset && TryOffset(rule.EventArguments.GetValueOrDefault("minutesBefore"), out var minutes))
                    offsets.Add((rule.Source.Id, rule.EventArguments.GetValueOrDefault("kind"), minutes));
            }
        return offsets;
    }

    internal async Task PublishDue(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (!await db.Set<ScheduledCalendarEvent>().AnyAsync(occurrence => occurrence.PublishedAtUtc == null && occurrence.DueAtUtc <= now, ct)) return;
        await db.Database.CreateExecutionStrategy().ExecuteAsync(async _ =>
        {
            db.ChangeTracker.Clear();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var due = await db.Set<ScheduledCalendarEvent>().Where(occurrence => occurrence.PublishedAtUtc == null && occurrence.DueAtUtc <= now)
                .OrderBy(occurrence => occurrence.DueAtUtc).ThenBy(occurrence => occurrence.EntryId)
                .Take(50).ToArrayAsync(ct);
            foreach (var occurrence in due)
            {
                var fields = new Dictionary<string, string> { ["kind"] = occurrence.EntryKind };
                var eventKind = occurrence.Event;
                if (eventKind.StartsWith($"{StartOffset}:", StringComparison.Ordinal))
                {
                    fields["minutesBefore"] = eventKind[(StartOffset.Length + 1)..];
                    eventKind = StartOffset;
                }
                await events.Enqueue(occurrence.OrganizationId, occurrence.ProjectId, Guid.Empty,
                    new(new("calendar-entry", occurrence.EntryId), eventKind, fields, [new("calendar")], occurrence.DueAtUtc), ct);
                occurrence.PublishedAtUtc = now;
            }
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, ct);
        db.ChangeTracker.Clear();
    }

    private static DateTimeOffset? Date(JsonElement entry, string dateProperty, string timeProperty, bool allDay, bool end, TimeZoneInfo zone)
    {
        if (!DateOnly.TryParseExact(entry.GetProperty(dateProperty).GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        var time = TimeOnly.MinValue;
        if (!allDay && !TimeOnly.TryParseExact(entry.GetProperty(timeProperty).GetString(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
            return null;
        if (allDay && end)
        {
            if (date == DateOnly.MaxValue) return null;
            date = date.AddDays(1);
        }

        var local = date.ToDateTime(time);
        try
        {
            // Resolve gaps forward and use the earlier instant for overlapping local times.
            while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
            var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
            return new DateTimeOffset(local, offset).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException) { return null; }
    }
}
