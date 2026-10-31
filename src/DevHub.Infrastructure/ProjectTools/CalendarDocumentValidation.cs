using System.Globalization;
using System.Text.Json;

namespace DevHub.Infrastructure.ProjectTools;

public static class CalendarDocumentValidation
{
    private static string? Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static bool Valid(JsonElement document)
    {
        var zone = Text(document, "timeZone");
        if (zone is null || zone.Length > 100 || zone != "UTC" && !zone.Contains('/')) return false;
        try { TimeZoneInfo.FindSystemTimeZoneById(zone); }
        catch (TimeZoneNotFoundException) { return false; }
        catch (InvalidTimeZoneException) { return false; }
        if (!document.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Array || events.GetArrayLength() > 2000) return false;
        var ids = new HashSet<string>();
        foreach (var item in events.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return false;
            var id = Text(item, "id"); var title = Text(item, "title"); var description = Text(item, "description");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !ids.Add(id) || string.IsNullOrWhiteSpace(title) || title.Length > 150 || description is null || description.Length > 5000 || Text(item, "kind") is not ("meeting" or "milestone" or "event")) return false;
            if (!DateOnly.TryParseExact(Text(item, "startDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start) || !DateOnly.TryParseExact(Text(item, "endDate"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end) || end < start) return false;
            if (!item.TryGetProperty("allDay", out var allDay) || allDay.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
            var from = Text(item, "startTime"); var to = Text(item, "endTime");
            if (allDay.GetBoolean()) { if (from != "" || to != "") return false; }
            else if (!TimeOnly.TryParseExact(from, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromTime) || !TimeOnly.TryParseExact(to, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toTime) || start == end && toTime <= fromTime) return false;
        }
        return true;
    }
}
