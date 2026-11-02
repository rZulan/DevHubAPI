using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGitHubRepositoryConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GitHubRepositoriesJson",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GitHubConnections",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GitHubUserId = table.Column<long>(type: "bigint", nullable: false),
                    Login = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RefreshExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GitHubConnections", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_GitHubConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GitHubConnections");

            migrationBuilder.DropColumn(
                name: "GitHubRepositoriesJson",
                table: "Projects");
        }
    }
}
