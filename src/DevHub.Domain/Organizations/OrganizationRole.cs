using DevHub.Domain.Common;

namespace DevHub.Domain.Organizations;

public sealed class OrganizationRole : BaseEntity
{
    private OrganizationRole()
    {
    }

    private OrganizationRole(
        Guid id,
        Guid organizationId,
        string name,
        string color,
        int position,
        bool isOwnerRole,
        bool isDefaultRole,
        IEnumerable<string> permissions,
        DateTimeOffset createdAtUtc)
        : base(id, createdAtUtc)
    {
        OrganizationId = organizationId;
        IsOwnerRole = isOwnerRole;
        IsDefaultRole = isDefaultRole;
        Update(name, color, position, permissions, createdAtUtc);
    }

    public Guid OrganizationId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Color { get; private set; } = "#64748b";
    public int Position { get; private set; }
    public bool IsOwnerRole { get; private set; }
    public bool IsDefaultRole { get; private set; }
    public string PermissionsValue { get; private set; } = string.Empty;
    public Organization Organization { get; private set; } = null!;
    public IReadOnlyList<string> Permissions => PermissionsValue
        .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static OrganizationRole CreateOwner(
        Guid organizationId,
        DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), organizationId, "Org Owner", "#f59e0b", 0, true, false,
            OrganizationPermissions.All, createdAtUtc);

    public static OrganizationRole CreateMember(
        Guid organizationId,
        DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), organizationId, "Member", "#34d399", 1000, false, true,
            [], createdAtUtc);

    public static OrganizationRole Create(
        Guid organizationId,
        string name,
        string color,
        int position,
        IEnumerable<string> permissions,
        DateTimeOffset createdAtUtc) =>
        new(Guid.NewGuid(), organizationId, name, color, position, false, false,
            permissions, createdAtUtc);

    public void Update(
        string name,
        string color,
        int position,
        IEnumerable<string> permissions,
        DateTimeOffset updatedAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        Color = string.IsNullOrWhiteSpace(color) ? "#64748b" : color.Trim();
        Position = IsOwnerRole ? 0 : IsDefaultRole ? 1000 : Math.Clamp(position, 1, 999);
        var requestedPermissions = permissions.ToArray();
        var effectivePermissions = IsOwnerRole || requestedPermissions.Contains(OrganizationPermissions.Administrator)
            ? OrganizationPermissions.All
            : requestedPermissions;
        PermissionsValue = string.Join('|', effectivePermissions
            .Where(OrganizationPermissions.All.Contains)
            .Distinct(StringComparer.Ordinal));
        MarkUpdated(updatedAtUtc);
    }

    public bool HasPermission(string permission) =>
        Permissions.Contains(OrganizationPermissions.Administrator) ||
        Permissions.Contains(permission);
}
