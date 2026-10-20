using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotesJson",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotesRevision",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotesJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "NotesRevision",
                table: "Projects");
        }
    }
}
