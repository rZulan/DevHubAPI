using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberColorSchemes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ColorSchemeId",
                table: "OrganizationMembers",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "default");

            migrationBuilder.CreateTable(
                name: "OrganizationColorSchemes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DarkAccent = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    DarkBackground = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    DarkSidebar = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    DarkSurface = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    DarkText = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    LightAccent = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    LightBackground = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    LightSidebar = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    LightSurface = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    LightText = table.Column<string>(type: "varchar(7)", unicode: false, maxLength: 7, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationColorSchemes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationColorSchemes_OrganizationMembers_OrganizationId_UserId",
                        columns: x => new { x.OrganizationId, x.UserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationColorSchemes_OrganizationId_UserId_Name",
                table: "OrganizationColorSchemes",
                columns: new[] { "OrganizationId", "UserId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationColorSchemes");

            migrationBuilder.DropColumn(
                name: "ColorSchemeId",
                table: "OrganizationMembers");
        }
    }
}
