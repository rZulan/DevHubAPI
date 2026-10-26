namespace DevHub.Domain.Organizations;

public static class OrganizationPermissions
{
    public const string Administrator = "Administrator";
    public const string ManageOrganization = "Manage organization";
    public const string ManageRoles = "Manage roles";
    public const string ManageMembers = "Manage members";
    public const string ChangeNicknames = "Change nicknames";
    public const string CreateInvites = "Create invites";
    public const string ManageTeams = "Manage teams";
    public const string ViewAllProjects = "View all projects";
    public const string ManageProjects = "Manage projects";
    public const string ManageTasks = "Manage tasks";
    public const string EditIdeation = "Edit ideation";
    public const string EditErd = "Edit ERD";
    public const string EditUiDesigns = "Edit UI designs";
    public const string ManageNotes = "Manage notes";
    public const string ManageWiki = "Manage wiki";
    public const string ManageBugReports = "Manage bug reports";
    public const string ManageRoadmap = "Manage roadmap";
    public const string ManageDashboard = "Manage dashboard";
    public const string ManageInvites = "Manage invites";

    // Preserve existing broad grants while allowing new roles to receive narrower access.
    public static bool Grants(IEnumerable<string> permissions, string permission)
    {
        var granted = permissions.ToHashSet(StringComparer.Ordinal);
        return granted.Contains(Administrator) || granted.Contains(permission) || (permission switch
        {
            EditErd or EditUiDesigns or ManageNotes => granted.Contains(EditIdeation),
            ManageRoadmap => granted.Contains(ManageProjects),
            ManageDashboard => granted.Contains(ManageOrganization),
            CreateInvites => granted.Contains(ManageInvites),
            _ => false
        });
    }

    public static readonly IReadOnlyList<string> All =
    [
        Administrator,
        ManageOrganization,
        ManageRoles,
        ManageMembers,
        ChangeNicknames,
        CreateInvites,
        ManageTeams,
        ViewAllProjects,
        ManageProjects,
        ManageTasks,
        EditIdeation,
        EditErd,
        EditUiDesigns,
        ManageNotes,
        ManageWiki,
        ManageBugReports,
        ManageRoadmap,
        ManageDashboard,
        ManageInvites
    ];
}
