using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalDashboards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PersonalDashboardLayoutJson",
                table: "OrganizationMembers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PersonalDashboardSavedAtUtc",
                table: "OrganizationMembers",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PersonalDashboardLayoutJson",
                table: "OrganizationMembers");

            migrationBuilder.DropColumn(
                name: "PersonalDashboardSavedAtUtc",
                table: "OrganizationMembers");
        }
    }
}
