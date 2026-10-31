using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnforceSingleOrganizationOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM Organizations o LEFT JOIN OrganizationMembers m
                    ON m.OrganizationId = o.Id AND m.UserId = o.OwnerUserId WHERE m.UserId IS NULL)
                    THROW 51000, 'The recorded owner must be an organization member before ownership can be normalized.', 1;

                UPDATE m SET IsOwner = 0 FROM OrganizationMembers m JOIN Organizations o ON o.Id = m.OrganizationId
                    WHERE m.IsOwner = 1 AND m.UserId <> o.OwnerUserId;
                UPDATE m SET IsOwner = 1 FROM OrganizationMembers m JOIN Organizations o ON o.Id = m.OrganizationId
                    WHERE m.UserId = o.OwnerUserId AND m.IsOwner = 0;
                DELETE a FROM OrganizationMemberRoles a JOIN OrganizationRoles r ON r.Id = a.RoleId AND r.OrganizationId = a.OrganizationId
                    JOIN Organizations o ON o.Id = a.OrganizationId WHERE r.IsOwnerRole = 1 AND a.UserId <> o.OwnerUserId;
                INSERT INTO OrganizationMemberRoles (OrganizationId, UserId, RoleId, AssignedAtUtc)
                    SELECT o.Id, o.OwnerUserId, r.Id, SYSUTCDATETIME() FROM Organizations o
                    JOIN OrganizationRoles r ON r.OrganizationId = o.Id AND r.IsOwnerRole = 1
                    WHERE NOT EXISTS (SELECT 1 FROM OrganizationMemberRoles a WHERE a.OrganizationId = o.Id AND a.UserId = o.OwnerUserId AND a.RoleId = r.Id);
                """);
            migrationBuilder.CreateIndex(
                name: "UX_OrganizationMembers_SingleOwner",
                table: "OrganizationMembers",
                column: "OrganizationId",
                unique: true,
                filter: "[IsOwner] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_OrganizationMembers_SingleOwner",
                table: "OrganizationMembers");
        }
    }
}
