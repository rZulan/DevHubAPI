using DevHub.Application.Abstractions.Persistence;
using DevHub.Application.Appearance;
using DevHub.Application.Common;
using DevHub.Domain.Organizations;
using DevHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DevHub.Infrastructure.Appearance;

internal sealed class MemberAppearanceService(
    ApplicationDbContext db,
    TimeProvider timeProvider) : IMemberAppearanceService
{
    private static readonly Error Missing = new("Appearance.NotFound", "The organization was not found or you are not one of its members.", ErrorType.NotFound);
    private static readonly Error SchemeMissing = new("Appearance.SchemeNotFound", "The color scheme was not found.", ErrorType.NotFound);
    private static readonly Error DuplicateName = new("Appearance.NameAlreadyExists", "You already have a color scheme with this name.", ErrorType.Conflict);
    private static readonly Error LimitReached = new("Appearance.LimitReached", $"You can have at most {ColorSchemePresets.MaxCustomSchemes} custom color schemes.", ErrorType.Conflict);

    public async Task<Result<MemberAppearanceResponse>> Get(Guid organizationId, Guid userId, CancellationToken ct)
    {
        var activeSchemeId = await ActiveSchemeId(organizationId, userId, ct);
        return activeSchemeId is null
            ? Result<MemberAppearanceResponse>.Failure(Missing)
            : Result<MemberAppearanceResponse>.Success(await Load(organizationId, userId, activeSchemeId, ct));
    }

    public async Task<Result<MemberAppearanceResponse>> SetActive(Guid organizationId, Guid userId, SetActiveColorSchemeRequest request, CancellationToken ct)
    {
        if (await ActiveSchemeId(organizationId, userId, ct) is null) return Result<MemberAppearanceResponse>.Failure(Missing);

        var schemeId = request.SchemeId?.Trim().ToLowerInvariant() ?? string.Empty;
        var exists = ColorSchemePresets.Contains(schemeId) ||
            (Guid.TryParse(schemeId, out var customId) && await OwnSchemes(organizationId, userId).AnyAsync(scheme => scheme.Id == customId, ct));
        if (!exists) return Result<MemberAppearanceResponse>.Failure(SchemeMissing);

        await Members(organizationId, userId)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.ColorSchemeId, schemeId), ct);
        return Result<MemberAppearanceResponse>.Success(await Load(organizationId, userId, schemeId, ct));
    }

    public async Task<Result<ColorSchemeResponse>> Create(Guid organizationId, Guid userId, SaveColorSchemeRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid) return Result<ColorSchemeResponse>.Failure(invalid);
        if (await ActiveSchemeId(organizationId, userId, ct) is null) return Result<ColorSchemeResponse>.Failure(Missing);
        if (await OwnSchemes(organizationId, userId).CountAsync(ct) >= ColorSchemePresets.MaxCustomSchemes)
            return Result<ColorSchemeResponse>.Failure(LimitReached);
        if (await NameTaken(organizationId, userId, request.Name, null, ct)) return Result<ColorSchemeResponse>.Failure(DuplicateName);

        var created = OrganizationColorScheme.Create(organizationId, userId, request.Name, request.Light, request.Dark, timeProvider.GetUtcNow());
        db.OrganizationColorSchemes.Add(created);
        return await Save(created, ct);
    }

    public async Task<Result<ColorSchemeResponse>> Update(Guid organizationId, Guid userId, Guid schemeId, SaveColorSchemeRequest request, CancellationToken ct)
    {
        if (Validate(request) is { } invalid) return Result<ColorSchemeResponse>.Failure(invalid);
        var scheme = await OwnSchemes(organizationId, userId).AsTracking().SingleOrDefaultAsync(candidate => candidate.Id == schemeId, ct);
        if (scheme is null) return Result<ColorSchemeResponse>.Failure(SchemeMissing);
        if (await NameTaken(organizationId, userId, request.Name, schemeId, ct)) return Result<ColorSchemeResponse>.Failure(DuplicateName);

        scheme.Update(request.Name, request.Light, request.Dark, timeProvider.GetUtcNow());
        return await Save(scheme, ct);
    }

    public async Task<Result<bool>> Delete(Guid organizationId, Guid userId, Guid schemeId, CancellationToken ct)
    {
        var deleted = await OwnSchemes(organizationId, userId)
            .Where(scheme => scheme.Id == schemeId)
            .ExecuteDeleteAsync(ct);
        if (deleted == 0) return Result<bool>.Failure(SchemeMissing);

        // Deleting the scheme in use simply returns this member to the default look.
        var deletedId = schemeId.ToString();
        await Members(organizationId, userId)
            .Where(member => member.ColorSchemeId == deletedId)
            .ExecuteUpdateAsync(update => update.SetProperty(member => member.ColorSchemeId, OrganizationColorScheme.DefaultId), ct);
        return Result<bool>.Success(true);
    }

    private IQueryable<OrganizationMember> Members(Guid organizationId, Guid userId) =>
        db.Set<OrganizationMember>().Where(member => member.OrganizationId == organizationId && member.UserId == userId);

    // Schemes are private: every lookup is scoped to the requesting member.
    private IQueryable<OrganizationColorScheme> OwnSchemes(Guid organizationId, Guid userId) =>
        db.OrganizationColorSchemes.AsNoTracking().Where(scheme => scheme.OrganizationId == organizationId && scheme.UserId == userId);

    /// <summary>Returns null when the user is not a member, which doubles as the access check.</summary>
    private Task<string?> ActiveSchemeId(Guid organizationId, Guid userId, CancellationToken ct) =>
        Members(organizationId, userId).AsNoTracking()
            .Select(member => member.ColorSchemeId)
            .SingleOrDefaultAsync(ct);

    private async Task<bool> NameTaken(Guid organizationId, Guid userId, string name, Guid? excludingId, CancellationToken ct)
    {
        var trimmed = name.Trim();
        return ColorSchemePresets.All.Any(preset => preset.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)) ||
            await OwnSchemes(organizationId, userId).AnyAsync(scheme =>
                scheme.Name == trimmed && (!excludingId.HasValue || scheme.Id != excludingId.Value), ct);
    }

    private async Task<Result<ColorSchemeResponse>> Save(OrganizationColorScheme scheme, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            return Result<ColorSchemeResponse>.Failure(DuplicateName);
        }

        return Result<ColorSchemeResponse>.Success(ToResponse(scheme));
    }

    private async Task<MemberAppearanceResponse> Load(Guid organizationId, Guid userId, string activeSchemeId, CancellationToken ct)
    {
        var customSchemes = await OwnSchemes(organizationId, userId)
            .OrderBy(scheme => scheme.CreatedAtUtc)
            .ToListAsync(ct);
        var resolvedSchemeId = ColorSchemePresets.Contains(activeSchemeId) ||
            customSchemes.Any(scheme => scheme.Id.ToString() == activeSchemeId)
                ? activeSchemeId
                : OrganizationColorScheme.DefaultId;

        return new MemberAppearanceResponse(
            resolvedSchemeId,
            ColorSchemePresets.MaxCustomSchemes,
            ColorSchemePresets.All,
            customSchemes.Select(ToResponse).ToArray());
    }

    private static Error? Validate(SaveColorSchemeRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > OrganizationColorScheme.NameMaxLength)
            errors[nameof(request.Name)] = [$"Give the color scheme a name of 1 to {OrganizationColorScheme.NameMaxLength} characters."];
        if (request.Light is null || !request.Light.IsValid())
            errors[nameof(request.Light)] = ["Every light mode color must be a six-digit hex color."];
        if (request.Dark is null || !request.Dark.IsValid())
            errors[nameof(request.Dark)] = ["Every dark mode color must be a six-digit hex color."];
        return errors.Count == 0
            ? null
            : new Error("Appearance.ValidationFailed", "One or more color scheme fields are invalid.", ErrorType.Validation, errors);
    }

    private static ColorSchemeResponse ToResponse(OrganizationColorScheme scheme) => new(
        scheme.Id.ToString(),
        scheme.Name,
        false,
        scheme.Light,
        scheme.Dark,
        scheme.UpdatedAtUtc);
}
