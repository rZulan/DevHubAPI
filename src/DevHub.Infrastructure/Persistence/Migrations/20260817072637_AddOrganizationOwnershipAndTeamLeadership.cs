using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrganizationOwnershipAndTeamLeadership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "LeaderUserId",
                table: "Teams",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "Organizations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE organization
                SET OwnerUserId = firstMember.UserId
                FROM Organizations AS organization
                CROSS APPLY (
                    SELECT TOP (1) member.UserId
                    FROM OrganizationMembers AS member
                    WHERE member.OrganizationId = organization.Id
                    ORDER BY member.JoinedAtUtc, member.UserId
                ) AS firstMember;

                IF EXISTS (SELECT 1 FROM Organizations WHERE OwnerUserId IS NULL)
                    THROW 51000, 'Every organization must have a member before ownership can be assigned.', 1;

                UPDATE team
                SET LeaderUserId = organization.OwnerUserId
                FROM Teams AS team
                INNER JOIN Organizations AS organization ON organization.Id = team.OrganizationId;

                INSERT INTO TeamMembers (TeamId, UserId, JoinedAtUtc)
                SELECT team.Id, team.LeaderUserId, team.CreatedAtUtc
                FROM Teams AS team
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM TeamMembers AS member
                    WHERE member.TeamId = team.Id AND member.UserId = team.LeaderUserId
                );
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "LeaderUserId",
                table: "Teams",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "OwnerUserId",
                table: "Organizations",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_LeaderUserId",
                table: "Teams",
                column: "LeaderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Organizations_OwnerUserId",
                table: "Organizations",
                column: "OwnerUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Organizations_Users_OwnerUserId",
                table: "Organizations",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_Users_LeaderUserId",
                table: "Teams",
                column: "LeaderUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Organizations_Users_OwnerUserId",
                table: "Organizations");

            migrationBuilder.DropForeignKey(
                name: "FK_Teams_Users_LeaderUserId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Teams_LeaderUserId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Organizations_OwnerUserId",
                table: "Organizations");

            migrationBuilder.DropColumn(
                name: "LeaderUserId",
                table: "Teams");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "Organizations");
        }
    }
}
