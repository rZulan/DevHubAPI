namespace DevHub.Domain.Organizations;

public static class OrganizationPermissions
{
    public const string Administrator = "Administrator";
    public const string ManageOrganization = "Manage organization";
    public const string ManageRoles = "Manage roles";
    public const string ManageMembers = "Manage members";
    public const string CreateInvites = "Create invites";
    public const string ManageTeams = "Manage teams";
    public const string ViewAllProjects = "View all projects";
    public const string ManageProjects = "Manage projects";
    public const string ManageTasks = "Manage tasks";
    public const string EditIdeation = "Edit ideation";

    public static readonly IReadOnlyList<string> All =
    [
        Administrator,
        ManageOrganization,
        ManageRoles,
        ManageMembers,
        CreateInvites,
        ManageTeams,
        ViewAllProjects,
        ManageProjects,
        ManageTasks,
        EditIdeation
    ];
}
