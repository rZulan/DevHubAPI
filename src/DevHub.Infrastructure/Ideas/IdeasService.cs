using System.Text.Json;
using System.Text.RegularExpressions;
using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Common;
using DevHub.Application.Ideas;
using DevHub.Domain.Organizations;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Ideas;

internal sealed class IdeasService(ApplicationDbContext db, IOrganizationRepository organizations) : IIdeasService
{
    private static readonly Error Missing = new("Ideas.NotFound", "The project was not found.", ErrorType.NotFound);
    private static readonly HashSet<string> AllowedFonts = new(StringComparer.Ordinal)
    {
        "Geist", "Arial", "Verdana", "Georgia", "Times New Roman", "Courier New",
        "Roboto", "Open Sans", "Lato", "Montserrat", "Poppins", "Inter", "Nunito", "Raleway", "Oswald", "Ubuntu",
        "Source Sans 3", "Noto Sans", "Merriweather", "Playfair Display", "Roboto Slab", "Noto Serif", "Libre Baskerville",
        "Roboto Mono", "Dancing Script", "Pacifico", "Bebas Neue"
    };
    private async Task<Error?> Access(Guid orgId, Guid projectId, Guid userId, bool write, CancellationToken ct)
    {
        var org = await organizations.GetByIdAsync(orgId, ct);
        if (org is null || !org.HasMember(userId)) return Missing;
        var project = await db.Projects.AsNoTracking().SingleOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == orgId, ct);
        if (project is null || !(org.HasPermission(userId, OrganizationPermissions.ViewAllProjects) || org.Teams.Any(t => t.Id == project.TeamId && t.HasMember(userId)))) return Missing;
        return write && !org.HasPermission(userId, OrganizationPermissions.EditIdeation)
            ? new Error("Ideas.Forbidden", "You need Edit ideation permission to save this canvas.", ErrorType.Forbidden) : null;
    }

    public async Task<Result<IdeasResponse>> Get(Guid organizationId, Guid projectId, Guid userId, CancellationToken ct)
    {
        if (await Access(organizationId, projectId, userId, false, ct) is { } error) return Result<IdeasResponse>.Failure(error);
        var project = await db.Projects.AsNoTracking().Where(p => p.Id == projectId && p.OrganizationId == organizationId).Select(p => new { p.IdeasJson, p.IdeasRevision }).SingleAsync(ct);
        return Result<IdeasResponse>.Success(new(project.IdeasRevision, JsonSerializer.Deserialize<JsonElement>(project.IdeasJson)));
    }

    public async Task<Result<IdeasResponse>> Save(Guid organizationId, Guid projectId, Guid userId, SaveIdeasRequest request, CancellationToken ct)
    {
        if (await Access(organizationId, projectId, userId, true, ct) is { } error) return Result<IdeasResponse>.Failure(error);
        if (request.Revision < 0 || request.Revision == int.MaxValue || !ValidItems(request.Items))
            return Result<IdeasResponse>.Failure(new Error("Ideas.Invalid", "The canvas contains invalid shapes or exceeds the supported document size.", ErrorType.Validation));
        var json = request.Items.GetRawText();
        var count = await db.Projects.Where(p => p.Id == projectId && p.OrganizationId == organizationId && p.IdeasRevision == request.Revision)
            .ExecuteUpdateAsync(update => update.SetProperty(p => p.IdeasJson, json).SetProperty(p => p.IdeasRevision, request.Revision + 1), ct);
        if (count == 0) return Result<IdeasResponse>.Failure(new Error("Ideas.Conflict", "This canvas was changed in another session. Reload the saved version before editing again.", ErrorType.Conflict));
        return Result<IdeasResponse>.Success(new(request.Revision + 1, request.Items));
    }

    private static bool ValidItems(JsonElement items)
    {
        if (items.ValueKind != JsonValueKind.Array || items.GetArrayLength() > 10000 || items.GetRawText().Length > 4_000_000) return false;
        var ids = new HashSet<string>();
        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) return false;
            string? Text(string name) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            bool Number(string name, double min, double max) => item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= min && number <= max;
            bool OptionalNumber(string name, double min, double max) => !item.TryGetProperty(name, out _) || Number(name, min, max);
            bool Color(string name) => Text(name) is { } value && Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$");
            bool RichRuns(string name)
            {
                if (!item.TryGetProperty(name, out var runs) || runs.ValueKind == JsonValueKind.Null) return true;
                if (runs.ValueKind != JsonValueKind.Array || runs.GetArrayLength() > 5000) return false;
                var length = 0;
                foreach (var run in runs.EnumerateArray())
                {
                    if (run.ValueKind != JsonValueKind.Object || !run.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String) return false;
                    length += text.GetString()?.Length ?? 0;
                    if (length > 100000) return false;
                    foreach (var flag in new[] { "bold", "italic", "underline", "strikethrough" })
                        if (!run.TryGetProperty(flag, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                    if (run.TryGetProperty("fontFamily", out var runFont) && runFont.ValueKind != JsonValueKind.Null
                        && (runFont.ValueKind != JsonValueKind.String || !AllowedFonts.Contains(runFont.GetString()!))) return false;
                    if (run.TryGetProperty("fontSize", out var runSize) && runSize.ValueKind != JsonValueKind.Null
                        && (runSize.ValueKind != JsonValueKind.Number || !runSize.TryGetDouble(out var size) || !double.IsFinite(size) || size < 1 || size > 1000)) return false;
                    if (run.TryGetProperty("letterSpacing", out var runSpacing) && runSpacing.ValueKind != JsonValueKind.Null
                        && (runSpacing.ValueKind != JsonValueKind.Number || !runSpacing.TryGetDouble(out var spacing) || !double.IsFinite(spacing) || spacing < -20 || spacing > 100)) return false;
                    if (run.TryGetProperty("textColor", out var runColor) && runColor.ValueKind != JsonValueKind.Null
                        && (runColor.ValueKind != JsonValueKind.String || runColor.GetString() is not { } color || color != "auto" && !Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"))) return false;
                }
                return true;
            }
            bool NoteTextStyle(string name)
            {
                if (!item.TryGetProperty(name, out var style) || style.ValueKind == JsonValueKind.Null) return true;
                if (style.ValueKind != JsonValueKind.Object) return false;
                string? StyleText(string property) => style.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                bool StyleNumber(string property, double min, double max) => style.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Number
                    && value.TryGetDouble(out var number) && double.IsFinite(number) && number >= min && number <= max;
                if (StyleText("fontFamily") is not { } font || !AllowedFonts.Contains(font)
                    || !StyleNumber("fontSize", 1, 1000) || !StyleNumber("letterSpacing", -20, 100)
                    || !StyleNumber("lineHeight", 0.5, 5) || !StyleNumber("textIndent", -500, 500)
                    || StyleText("textAlign") is not ("left" or "center" or "right")
                    || !(StyleText("textColor") == "auto" || StyleText("textColor") is { } textColor && Regex.IsMatch(textColor, "^#[0-9a-fA-F]{6}$"))) return false;
                foreach (var flag in new[] { "bold", "italic", "underline", "strikethrough" })
                    if (!style.TryGetProperty(flag, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
                return true;
            }
            var id = Text("id");
            if (string.IsNullOrWhiteSpace(id) || id.Length > 100 || !ids.Add(id)) return false;
            var kind = Text("kind") ?? "rectangle";
            var groupId = Text("groupId");
            if (kind is not ("rectangle" or "note") || groupId?.Length > 100) return false;
            if (!Number("x", -1e12, 1e12) || !Number("y", -1e12, 1e12) || !Number("width", 23.999999, 1e12) || !Number("height", 23.999999, 1e12)
                || !Number("cornerRadius", 0, 1e12) || !Number("outlineWidth", 1, 20) || !Number("fontSize", 1, 1000)
                || !OptionalNumber("letterSpacing", -20, 100) || !OptionalNumber("lineHeight", 0.5, 5) || !OptionalNumber("textIndent", -500, 500)) return false;
            if (Text("appearance") is not ("fill" or "outlined") || Text("outlineStyle") is not ("solid" or "dashed" or "dotted" or "double" or "none")
                || Text("textAlign") is not ("left" or "center" or "right") || Text("verticalAlign") is not ("top" or "center" or "bottom")
                || Text("fontFamily") is not { } fontFamily || !AllowedFonts.Contains(fontFamily)) return false;
            if (!Color("fillColor") || !Color("outlineColor") || !(Text("textColor") == "auto" || Color("textColor"))
                || Text("text") is not { Length: <= 100000 } || (Text("noteHeader") ?? "").Length > 10000 || (Text("noteBody") ?? "").Length > 100000
                || !RichRuns("textRuns") || !RichRuns("noteHeaderRuns") || !RichRuns("noteBodyRuns")
                || !NoteTextStyle("noteHeaderStyle") || !NoteTextStyle("noteBodyStyle")) return false;
            foreach (var flag in new[] { "bold", "italic", "underline", "strikethrough" })
                if (!item.TryGetProperty(flag, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false;
        }
        return true;
    }
}
