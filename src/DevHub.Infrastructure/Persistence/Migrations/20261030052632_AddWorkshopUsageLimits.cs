using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkshopUsageLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkshopUsageBuckets",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(180)", maxLength: 180, nullable: false),
                    WindowStartUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Used = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkshopUsageBuckets", x => new { x.Key, x.WindowStartUtc });
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkshopUsageBuckets_ExpiresAtUtc",
                table: "WorkshopUsageBuckets",
                column: "ExpiresAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkshopUsageBuckets");
        }
    }
}
