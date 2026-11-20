using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261120050031_AddColorSchemeGradients : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DarkGradient",
            table: "OrganizationColorSchemes",
            type: "varchar(160)",
            unicode: false,
            maxLength: 160,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LightGradient",
            table: "OrganizationColorSchemes",
            type: "varchar(160)",
            unicode: false,
            maxLength: 160,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "DarkGradient",
            table: "OrganizationColorSchemes");

        migrationBuilder.DropColumn(
            name: "LightGradient",
            table: "OrganizationColorSchemes");
    }
}
