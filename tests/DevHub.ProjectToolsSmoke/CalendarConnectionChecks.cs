using System.Globalization;
using System.Text.Json;
using DevHub.Application.Common;
using DevHub.Application.Connections;
using DevHub.Application.ProjectTools;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class CalendarConnectionChecks
{
    public static async Task Run(IServiceProvider provider, ConnectionScope scope, ConnectionNodeRef task, CancellationToken ct)
    {
        var tools = provider.GetRequiredService<IProjectToolsService>();
        var connections = provider.GetRequiredService<IProjectConnections>();
        var db = provider.GetRequiredService<ApplicationDbContext>();
        var march = new DateOnly(DateTime.UtcNow.Year + 2, 3, 1);
        var springTransition = march.AddDays((7 - (int)march.DayOfWeek) % 7 + 7);
        var date = springTransition.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var saved = Require(await tools.Get(scope.OrganizationId, scope.ProjectId, scope.UserId, "calendar", ct));
        saved = await Save(saved.Revision, "02:30");

        var entry = new ConnectionNodeRef("calendar-entry", "timed-fixture");
        var rule = new ConnectionRule(Guid.NewGuid(), "Calendar reminder", true, false, entry, "calendar-start-offset",
            new Dictionary<string, string> { ["minutesBefore"] = "30" }, false, [],
            [new(Guid.NewGuid(), task, "move-task", new Dictionary<string, string> { ["status"] = "done" })]);
        Require(await connections.Save(scope, null, new("Calendar reminder", 0, [entry, task], [rule]), ct));
        await CheckDue(6);

        saved = await Save(saved.Revision, "04:00");
        await CheckDue(7);

        await Save(saved.Revision, null);
        if ((await Due()).Length != 0) throw new InvalidOperationException("Deleting a Calendar entry kept its scheduled Connections offset.");
        Console.WriteLine("Connections Calendar offset, DST handling and timer invalidation checks passed.");

        async Task<ToolDocumentResponse> Save(int revision, string? start)
        {
            var entryData = new { id = "timed-fixture", title = "Calendar offset test", description = "", kind = "meeting", startDate = date,
                endDate = date, allDay = false, startTime = start, endTime = "04:30" };
            var document = JsonSerializer.SerializeToElement(new { timeZone = "America/New_York", events = start is null ? [] : new[] { entryData } });
            return Require(await tools.Save(scope.OrganizationId, scope.ProjectId, scope.UserId, "calendar", new(revision, document), ct));
        }

        async Task CheckDue(int hour)
        {
            var expected = new DateTimeOffset(springTransition.ToDateTime(new TimeOnly(hour, 30), DateTimeKind.Utc));
            var actual = await Due();
            if (actual.Length != 1 || actual[0] != expected)
                throw new InvalidOperationException("Calendar scheduling did not persist exactly one revised UTC offset occurrence.");
        }

        Task<DateTimeOffset[]> Due() => db.Database.SqlQuery<DateTimeOffset>($"""
            SELECT [DueAtUtc] AS [Value] FROM [ProjectConnectionCalendarSchedules]
            WHERE [ProjectId] = {scope.ProjectId} AND [EntryId] = 'timed-fixture' AND [Event] = 'calendar-start-offset:30'
            """).ToArrayAsync(ct);
    }

    private static T Require<T>(Result<T> result) => result.IsSuccess ? result.Value! : throw new InvalidOperationException(result.Error?.Description);
}
