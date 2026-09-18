using System.Text.RegularExpressions;
using DevHub.Domain.Common;

namespace DevHub.Domain.Organizations;

/// <summary>The five key colors a scheme defines for one light/dark mode; the client derives the rest.</summary>
public sealed partial record ColorSchemePalette(
    string Accent,
    string Background,
    string Surface,
    string Sidebar,
    string Text)
{
    public bool IsValid() =>
        IsHexColor(Accent) && IsHexColor(Background) && IsHexColor(Surface) &&
        IsHexColor(Sidebar) && IsHexColor(Text);

    public ColorSchemePalette Normalize() => new(
        Accent.ToLowerInvariant(),
        Background.ToLowerInvariant(),
        Surface.ToLowerInvariant(),
        Sidebar.ToLowerInvariant(),
        Text.ToLowerInvariant());

    public static bool IsHexColor(string? value) => value is not null && HexColor().IsMatch(value);

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexColor();
}

/// <summary>A custom color scheme that belongs to one member of an organization.</summary>
public sealed class OrganizationColorScheme : BaseEntity
{
    public const string DefaultId = "default";
    public const int NameMaxLength = 40;

    private OrganizationColorScheme()
    {
    }

    private OrganizationColorScheme(
        Guid id,
        Guid organizationId,
        Guid userId,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        OrganizationId = organizationId;
        UserId = userId;
    }

    public Guid OrganizationId { get; private set; }
    public Guid UserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public ColorSchemePalette Light { get; private set; } = null!;
    public ColorSchemePalette Dark { get; private set; } = null!;

    public static OrganizationColorScheme Create(
        Guid organizationId,
        Guid userId,
        string name,
        ColorSchemePalette light,
        ColorSchemePalette dark,
        DateTimeOffset createdAtUtc)
    {
        var scheme = new OrganizationColorScheme(Guid.NewGuid(), organizationId, userId, createdAtUtc);
        scheme.SetDetails(name, light, dark);
        return scheme;
    }

    public void Update(
        string name,
        ColorSchemePalette light,
        ColorSchemePalette dark,
        DateTimeOffset updatedAtUtc)
    {
        SetDetails(name, light, dark);
        MarkUpdated(updatedAtUtc);
    }

    private void SetDetails(string name, ColorSchemePalette light, ColorSchemePalette dark)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Trim().Length > NameMaxLength)
            throw new ArgumentOutOfRangeException(nameof(name), $"Color scheme names are limited to {NameMaxLength} characters.");
        if (!light.IsValid() || !dark.IsValid())
            throw new ArgumentException("Color scheme palettes must use six-digit hex colors.");

        Name = name.Trim();
        Light = light.Normalize();
        Dark = dark.Normalize();
    }
}
