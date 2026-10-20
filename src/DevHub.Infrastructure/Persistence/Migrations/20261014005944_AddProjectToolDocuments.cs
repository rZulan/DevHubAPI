using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectToolDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DesignJson",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DesignRevision",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ErdJson",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ErdRevision",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "RoadmapJson",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoadmapRevision",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DesignJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "DesignRevision",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ErdJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "ErdRevision",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RoadmapJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RoadmapRevision",
                table: "Projects");
        }
    }
}
