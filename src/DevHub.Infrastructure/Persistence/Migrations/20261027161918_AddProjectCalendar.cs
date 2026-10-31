using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectCalendar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectDocuments_ToolType",
                table: "ProjectDocuments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectDocuments_ToolType",
                table: "ProjectDocuments",
                sql: "[ToolType] IN ('ideas', 'erd', 'ui-editor', 'sdlc', 'notes', 'wiki', 'calendar')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_ProjectDocuments_ToolType",
                table: "ProjectDocuments");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProjectDocuments_ToolType",
                table: "ProjectDocuments",
                sql: "[ToolType] IN ('ideas', 'erd', 'ui-editor', 'sdlc', 'notes', 'wiki')");
        }
    }
}
