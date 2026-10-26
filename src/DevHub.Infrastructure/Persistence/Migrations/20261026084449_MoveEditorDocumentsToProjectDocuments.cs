using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveEditorDocumentsToProjectDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ContentJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDocuments", x => x.Id);
                    table.CheckConstraint("CK_ProjectDocuments_ContentJson", "[ContentJson] IS NULL OR ISJSON([ContentJson]) = 1");
                    table.CheckConstraint("CK_ProjectDocuments_Revision", "[Revision] >= 0");
                    table.CheckConstraint("CK_ProjectDocuments_ToolType", "[ToolType] IN ('ideas', 'erd', 'ui-editor', 'sdlc', 'notes', 'wiki')");
                    table.ForeignKey(
                        name: "FK_ProjectDocuments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectDocuments_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_ProjectId_ToolType",
                table: "ProjectDocuments",
                columns: new[] { "ProjectId", "ToolType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDocuments_UpdatedByUserId",
                table: "ProjectDocuments",
                column: "UpdatedByUserId");

            // Copy every tool, including empty documents, before removing legacy columns.
            // Keep content and revisions verbatim; historical save attribution is unknown.
            migrationBuilder.Sql("""
                INSERT INTO [ProjectDocuments] ([Id], [ProjectId], [ToolType], [ContentJson], [Revision])
                SELECT NEWID(), [project].[Id], [document].[ToolType], [document].[ContentJson], [document].[Revision]
                FROM [Projects] AS [project]
                CROSS APPLY (VALUES
                    (N'ideas', [project].[IdeasJson], [project].[IdeasRevision]),
                    (N'erd', [project].[ErdJson], [project].[ErdRevision]),
                    (N'ui-editor', [project].[DesignJson], [project].[DesignRevision]),
                    (N'sdlc', [project].[RoadmapJson], [project].[RoadmapRevision]),
                    (N'notes', [project].[NotesJson], [project].[NotesRevision]),
                    (N'wiki', [project].[WikiJson], [project].[WikiRevision])
                ) AS [document] ([ToolType], [ContentJson], [Revision]);
                """);

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
                name: "IdeasJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "IdeasRevision",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "NotesJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "NotesRevision",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RoadmapJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "RoadmapRevision",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "WikiJson",
                table: "Projects");

            migrationBuilder.DropColumn(
                name: "WikiRevision",
                table: "Projects");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                name: "IdeasJson",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "IdeasRevision",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "NotesJson",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NotesRevision",
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

            migrationBuilder.AddColumn<string>(
                name: "WikiJson",
                table: "Projects",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WikiRevision",
                table: "Projects",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Restore the latest saved content when rolling back the storage refactor.
            foreach (var (tool, json, revision) in new[]
            {
                ("ideas", "IdeasJson", "IdeasRevision"),
                ("erd", "ErdJson", "ErdRevision"),
                ("ui-editor", "DesignJson", "DesignRevision"),
                ("sdlc", "RoadmapJson", "RoadmapRevision"),
                ("notes", "NotesJson", "NotesRevision"),
                ("wiki", "WikiJson", "WikiRevision")
            })
            {
                migrationBuilder.Sql($"""
                    UPDATE [project]
                    SET [{json}] = {(tool == "ideas" ? "COALESCE([document].[ContentJson], N'[]')" : "[document].[ContentJson]")},
                        [{revision}] = [document].[Revision]
                    FROM [Projects] AS [project]
                    INNER JOIN [ProjectDocuments] AS [document] ON [document].[ProjectId] = [project].[Id]
                    WHERE [document].[ToolType] = N'{tool}';
                    """);
            }
            migrationBuilder.DropTable(name: "ProjectDocuments");
        }
    }
}
