using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PomodoroRelax4.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class TaskDueDatePriorityNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "DueDate",
                table: "PomodoroTasks",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "PomodoroTasks",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "PomodoroTasks",
                type: "integer",
                nullable: false,
                defaultValue: 1); // TaskPriority.Normal for existing tasks
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "PomodoroTasks");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "PomodoroTasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "PomodoroTasks");
        }
    }
}
