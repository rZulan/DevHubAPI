using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class _20261117030439_AddProjectConnections : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProjectConnectionCalendarSchedules",
            columns: table => new
            {
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EntryId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, collation: "Latin1_General_100_BIN2"),
                Event = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                EntryKind = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                DueAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectConnectionCalendarSchedules", x => new { x.ProjectId, x.EntryId, x.Event });
                table.ForeignKey(
                    name: "FK_ProjectConnectionCalendarSchedules_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProjectConnectionEvents",
            columns: table => new
            {
                Position = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                DraftJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                ChainJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                RootPosition = table.Column<long>(type: "bigint", nullable: true),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                FinishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                LeaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                LeasedUntilUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                Attempts = table.Column<int>(type: "int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectConnectionEvents", x => x.Position);
                table.ForeignKey(
                    name: "FK_ProjectConnectionEvents_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProjectConnectionRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConnectionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                RuleId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ConnectionRevision = table.Column<int>(type: "int", nullable: false),
                EventPosition = table.Column<long>(type: "bigint", nullable: false),
                RootPosition = table.Column<long>(type: "bigint", nullable: false),
                RuleJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Event = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                Detail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                StepsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                Version = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectConnectionRuns", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectConnectionRuns_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ProjectConnections",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                Revision = table.Column<int>(type: "int", nullable: false),
                NodesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                RulesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                Deleted = table.Column<bool>(type: "bit", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProjectConnections", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProjectConnections_Projects_ProjectId",
                    column: x => x.ProjectId,
                    principalTable: "Projects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectConnectionCalendarSchedules_PublishedAtUtc_DueAtUtc",
            table: "ProjectConnectionCalendarSchedules",
            columns: new[] { "PublishedAtUtc", "DueAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectConnectionEvents_FinishedAtUtc_Position",
            table: "ProjectConnectionEvents",
            columns: new[] { "FinishedAtUtc", "Position" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectConnectionEvents_ProjectId_Position",
            table: "ProjectConnectionEvents",
            columns: new[] { "ProjectId", "Position" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectConnectionRuns_ConnectionId_RuleId_EventPosition",
            table: "ProjectConnectionRuns",
            columns: new[] { "ConnectionId", "RuleId", "EventPosition" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ProjectConnectionRuns_ProjectId_OccurredAtUtc",
            table: "ProjectConnectionRuns",
            columns: new[] { "ProjectId", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectConnectionRuns_ProjectId_RootPosition",
            table: "ProjectConnectionRuns",
            columns: new[] { "ProjectId", "RootPosition" });

        migrationBuilder.CreateIndex(
            name: "IX_ProjectConnections_ProjectId_Deleted",
            table: "ProjectConnections",
            columns: new[] { "ProjectId", "Deleted" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ProjectConnectionCalendarSchedules");

        migrationBuilder.DropTable(
            name: "ProjectConnectionEvents");

        migrationBuilder.DropTable(
            name: "ProjectConnectionRuns");

        migrationBuilder.DropTable(
            name: "ProjectConnections");
    }
}
