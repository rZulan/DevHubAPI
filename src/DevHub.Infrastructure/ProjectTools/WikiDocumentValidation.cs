using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DevHub.Infrastructure.ProjectTools;

internal static class WikiDocumentValidation
{
    private static string? Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static bool String(JsonElement item, string key, int max, bool required = false) => Text(item, key) is { } text && text.Length <= max && (!required || !string.IsNullOrWhiteSpace(text));
    private static bool Revision(JsonElement item) => item.ValueKind == JsonValueKind.Object && String(item, "id", 100, true)
        && String(item, "title", 150, true) && String(item, "body", 100000) && String(item, "message", 500)
        && String(item, "authorId", 100, true) && String(item, "updatedAt", 100, true)
        && DateTimeOffset.TryParse(Text(item, "updatedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    public static bool Valid(JsonElement document)
    {
        if (!document.TryGetProperty("pages", out var pages) || pages.ValueKind != JsonValueKind.Array || pages.GetArrayLength() > 100
            || !document.TryGetProperty("homePageId", out var home)) return false;
        var ids = new HashSet<string>(); var slugs = new HashSet<string>(); var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parents = new Dictionary<string, string?>();
        foreach (var page in pages.EnumerateArray())
        {
            if (!Revision(page) || !ids.Add(Text(page, "id")!) || !titles.Add(Text(page, "title")!.Trim())
                || Text(page, "slug") is not {Length: > 0 and <= 100} slug || !Regex.IsMatch(slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$") || !slugs.Add(slug)
                || !page.TryGetProperty("history", out var history) || history.ValueKind != JsonValueKind.Array || history.GetArrayLength() > 20) return false;
            var revisionIds = new HashSet<string>();
            foreach (var revision in history.EnumerateArray()) if (!Revision(revision) || !revisionIds.Add(Text(revision, "id")!)) return false;
            if (page.TryGetProperty("parentId", out var parent) && parent.ValueKind != JsonValueKind.Null
                && !String(page, "parentId", 100, true)) return false;
            parents.Add(Text(page, "id")!, Text(page, "parentId"));
        }
        foreach (var id in ids)
        {
            var seen = new HashSet<string> { id };
            var parent = parents[id];
            while (parent is not null)
            {
                if (!parents.ContainsKey(parent) || !seen.Add(parent) || seen.Count > 3) return false;
                parent = parents[parent];
            }
        }
        return ids.Count == 0 ? home.ValueKind == JsonValueKind.Null : home.ValueKind == JsonValueKind.String && ids.Contains(home.GetString()!);
    }
}
