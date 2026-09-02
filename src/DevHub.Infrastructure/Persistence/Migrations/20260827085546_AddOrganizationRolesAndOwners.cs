using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationRolesAndOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOwner",
                table: "OrganizationMembers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "OrganizationRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Position = table.Column<int>(type: "int", nullable: false),
                    IsOwnerRole = table.Column<bool>(type: "bit", nullable: false),
                    IsDefaultRole = table.Column<bool>(type: "bit", nullable: false),
                    PermissionsValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationRoles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrganizationRoles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrganizationMemberRoles",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RoleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AssignedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrganizationMemberRoles", x => new { x.OrganizationId, x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_OrganizationMemberRoles_OrganizationMembers_OrganizationId_UserId",
                        columns: x => new { x.OrganizationId, x.UserId },
                        principalTable: "OrganizationMembers",
                        principalColumns: new[] { "OrganizationId", "UserId" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrganizationMemberRoles_OrganizationRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "OrganizationRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.Sql(
                """
                UPDATE member
                SET IsOwner = 1
                FROM OrganizationMembers AS member
                INNER JOIN Organizations AS organization
                    ON organization.Id = member.OrganizationId
                   AND organization.OwnerUserId = member.UserId;

                INSERT INTO OrganizationRoles
                    (Id, OrganizationId, Name, Color, Position, IsOwnerRole, IsDefaultRole, PermissionsValue, CreatedAtUtc, UpdatedAtUtc)
                SELECT NEWID(), Id, 'Org Owner', '#f59e0b', 0, 1, 0,
                    'Administrator|Manage organization|Manage roles|Manage members|Create invites|Manage teams|View all projects|Manage projects|Manage tasks|Edit ideation',
                    CreatedAtUtc, NULL
                FROM Organizations;

                INSERT INTO OrganizationRoles
                    (Id, OrganizationId, Name, Color, Position, IsOwnerRole, IsDefaultRole, PermissionsValue, CreatedAtUtc, UpdatedAtUtc)
                SELECT NEWID(), Id, 'Member', '#34d399', 1000, 0, 1, '', CreatedAtUtc, NULL
                FROM Organizations;

                INSERT INTO OrganizationMemberRoles (OrganizationId, UserId, RoleId, AssignedAtUtc)
                SELECT member.OrganizationId, member.UserId, role.Id, member.JoinedAtUtc
                FROM OrganizationMembers AS member
                INNER JOIN OrganizationRoles AS role
                    ON role.OrganizationId = member.OrganizationId
                   AND role.IsDefaultRole = 1;

                INSERT INTO OrganizationMemberRoles (OrganizationId, UserId, RoleId, AssignedAtUtc)
                SELECT member.OrganizationId, member.UserId, role.Id, member.JoinedAtUtc
                FROM OrganizationMembers AS member
                INNER JOIN OrganizationRoles AS role
                    ON role.OrganizationId = member.OrganizationId
                   AND role.IsOwnerRole = 1
                WHERE member.IsOwner = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationMemberRoles_RoleId",
                table: "OrganizationMemberRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRoles_OrganizationId_Name",
                table: "OrganizationRoles",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationRoles_OrganizationId_Position",
                table: "OrganizationRoles",
                columns: new[] { "OrganizationId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OrganizationMemberRoles");

            migrationBuilder.DropTable(
                name: "OrganizationRoles");

            migrationBuilder.DropColumn(
                name: "IsOwner",
                table: "OrganizationMembers");
        }
    }
}
