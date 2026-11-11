using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResetDashboardLayoutsForNewDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [Organizations]
                SET [DashboardLayoutJson] = NULL, [DashboardPublishedAtUtc] = NULL
                WHERE [DashboardLayoutJson] IS NOT NULL OR [DashboardPublishedAtUtc] IS NOT NULL;

                UPDATE [OrganizationMembers]
                SET [PersonalDashboardLayoutJson] = NULL, [PersonalDashboardSavedAtUtc] = NULL
                WHERE [PersonalDashboardLayoutJson] IS NOT NULL OR [PersonalDashboardSavedAtUtc] IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Previous custom layouts can be recovered only from a pre-migration backup.
        }
    }
}
