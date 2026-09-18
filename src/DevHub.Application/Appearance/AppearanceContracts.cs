using DevHub.Application.Common;
using DevHub.Domain.Organizations;

namespace DevHub.Application.Appearance;

public sealed record ColorSchemeResponse(
    string Id,
    string Name,
    bool IsPreset,
    ColorSchemePalette Light,
    ColorSchemePalette Dark,
    DateTimeOffset? UpdatedAtUtc);

/// <summary>The requesting member's personal workshop appearance; other members never see it.</summary>
public sealed record MemberAppearanceResponse(
    string ActiveSchemeId,
    int MaxCustomSchemes,
    IReadOnlyList<ColorSchemeResponse> Presets,
    IReadOnlyList<ColorSchemeResponse> CustomSchemes);

public sealed record SetActiveColorSchemeRequest(string SchemeId);

public sealed record SaveColorSchemeRequest(string Name, ColorSchemePalette Light, ColorSchemePalette Dark);

public interface IMemberAppearanceService
{
    Task<Result<MemberAppearanceResponse>> Get(Guid organizationId, Guid userId, CancellationToken ct);
    Task<Result<MemberAppearanceResponse>> SetActive(Guid organizationId, Guid userId, SetActiveColorSchemeRequest request, CancellationToken ct);
    Task<Result<ColorSchemeResponse>> Create(Guid organizationId, Guid userId, SaveColorSchemeRequest request, CancellationToken ct);
    Task<Result<ColorSchemeResponse>> Update(Guid organizationId, Guid userId, Guid schemeId, SaveColorSchemeRequest request, CancellationToken ct);
    Task<Result<bool>> Delete(Guid organizationId, Guid userId, Guid schemeId, CancellationToken ct);
}

public static class ColorSchemePresets
{
    public const int MaxCustomSchemes = 20;

    public static readonly IReadOnlyList<ColorSchemeResponse> All =
    [
        Preset(OrganizationColorScheme.DefaultId, "Graphite",
            new("#171717", "#ffffff", "#ffffff", "#fafafa", "#0a0a0a"),
            new("#e5e5e5", "#0a0a0a", "#171717", "#171717", "#fafafa")),
        Preset("ocean", "Ocean",
            new("#2563eb", "#f8fafc", "#ffffff", "#f1f5f9", "#0f172a"),
            new("#60a5fa", "#0b1220", "#111b2e", "#0d1627", "#e2e8f0")),
        Preset("forest", "Forest",
            new("#15803d", "#f7faf8", "#ffffff", "#eef5f0", "#10231a"),
            new("#4ade80", "#0a130e", "#112018", "#0d1a13", "#e2efe7")),
        Preset("sunset", "Sunset",
            new("#c2410c", "#fffaf5", "#ffffff", "#fdf2e9", "#2b1a10"),
            new("#fb923c", "#140e0b", "#1f1712", "#19120e", "#f6ebe2")),
        Preset("orchid", "Orchid",
            new("#7c3aed", "#faf9ff", "#ffffff", "#f3f0fd", "#1d1733"),
            new("#a78bfa", "#0f0c1a", "#181426", "#13101f", "#ece9f7")),
    ];

    public static bool Contains(string schemeId) => All.Any(preset => preset.Id == schemeId);

    private static ColorSchemeResponse Preset(
        string id,
        string name,
        ColorSchemePalette light,
        ColorSchemePalette dark) =>
        new(id, name, true, light, dark, null);
}
