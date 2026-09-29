using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DevHub.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomizeTodoBoards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoardRevision",
                table: "Todos",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ColumnsJson",
                table: "Todos",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[{\"Id\":\"pending\",\"Name\":\"Pending\"},{\"Id\":\"progress\",\"Name\":\"In progress\"},{\"Id\":\"done\",\"Name\":\"Done\"}]");

            migrationBuilder.CreateTable(
                name: "TodoTaskAssignees",
                columns: table => new
                {
                    TaskId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TodoTaskAssignees", x => new { x.TaskId, x.UserId });
                    table.ForeignKey(
                        name: "FK_TodoTaskAssignees_TodoTasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "TodoTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TodoTaskAssignees_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_TodoTaskAssignees_UserId",
                table: "TodoTaskAssignees",
                column: "UserId");

            migrationBuilder.Sql("""
                INSERT INTO TodoTaskAssignees (TaskId, UserId)
                SELECT Id, AssigneeId FROM TodoTasks WHERE AssigneeId IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TodoTaskAssignees");

            migrationBuilder.DropColumn(
                name: "BoardRevision",
                table: "Todos");

            migrationBuilder.DropColumn(
                name: "ColumnsJson",
                table: "Todos");
        }
    }
}
