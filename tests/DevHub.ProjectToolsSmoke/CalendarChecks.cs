using System.Text.Json;
using DevHub.Infrastructure.ProjectTools;

internal static class CalendarChecks
{
    public static void Run()
    {
        const string valid = """{"timeZone":"Asia/Manila","events":[{"id":"review","title":"Release review","description":"Agenda","kind":"meeting","startDate":"2026-10-28","endDate":"2026-10-28","allDay":false,"startTime":"09:00","endTime":"10:00"}]}""";
        bool Valid(string json) => ToolDocumentValidation.Valid("calendar", JsonSerializer.Deserialize<JsonElement>(json));
        void Check(bool condition, string name) { if (!condition) throw new Exception(name); Console.WriteLine($"PASS: {name}"); }
        Check(Valid(valid), "Calendar accepts a timed meeting with an IANA time zone");
        Check(Valid("""{"timeZone":"UTC","events":[]}"""), "Calendar accepts an empty document");
        Check(!Valid(valid.Replace("Asia/Manila", "Pacific Standard Time")), "Calendar rejects time zones the browser cannot display");
        Check(Valid(valid.Replace("\"allDay\":false", "\"allDay\":true").Replace("09:00", "").Replace("10:00", "")), "Calendar accepts all-day events");
        foreach (var (before, after) in new[] {("Release review", " "), ("Asia/Manila", "invalid"), ("2026-10-28", "2026-02-30"), ("10:00", "08:00"), ("09:00", "24:00"), ("meeting", "todo")})
            Check(!Valid(valid.Replace(before, after)), $"Calendar rejects invalid {before} replacement");
        var overnight = valid.Replace("\"endDate\":\"2026-10-28\"", "\"endDate\":\"2026-10-29\"");
        Check(Valid(overnight.Replace("10:00", "00:00")), "Calendar accepts midnight on the explicit end date");
        Check(!Valid(overnight.Replace("10:00", "24:00")), "Calendar rejects a wrapped midnight on the end date");
        Check(!Valid(valid.Replace("\"allDay\":false", "\"allDay\":true")), "All-day calendar events cannot retain hidden times");
        var entry = JsonSerializer.Deserialize<JsonElement>(valid).GetProperty("events")[0].GetRawText();
        Check(!Valid("{\"timeZone\":\"UTC\",\"events\":[" + entry + "," + entry + "]}"), "Calendar rejects duplicate event IDs");
    }
}
