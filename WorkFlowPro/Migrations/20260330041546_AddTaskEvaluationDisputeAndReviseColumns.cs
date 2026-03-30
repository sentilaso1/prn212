using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkFlowPro.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskEvaluationDisputeAndReviseColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisputeReason",
                table: "TaskEvaluations",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DisputedAtUtc",
                table: "TaskEvaluations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DisputedByUserId",
                table: "TaskEvaluations",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LevelOverride",
                table: "TaskEvaluations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OriginalScore",
                table: "TaskEvaluations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "RevisedAtUtc",
                table: "TaskEvaluations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RevisedReason",
                table: "TaskEvaluations",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.Sql("UPDATE TaskEvaluations SET OriginalScore = Score;");

            migrationBuilder.CreateIndex(
                name: "IX_TaskEvaluations_DisputedByUserId",
                table: "TaskEvaluations",
                column: "DisputedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskEvaluations_IsLocked_EvaluatedAtUtc",
                table: "TaskEvaluations",
                columns: new[] { "IsLocked", "EvaluatedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_TaskEvaluations_AspNetUsers_DisputedByUserId",
                table: "TaskEvaluations",
                column: "DisputedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TaskEvaluations_AspNetUsers_DisputedByUserId",
                table: "TaskEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_TaskEvaluations_DisputedByUserId",
                table: "TaskEvaluations");

            migrationBuilder.DropIndex(
                name: "IX_TaskEvaluations_IsLocked_EvaluatedAtUtc",
                table: "TaskEvaluations");

            migrationBuilder.DropColumn(
                name: "DisputeReason",
                table: "TaskEvaluations");

            migrationBuilder.DropColumn(
                name: "DisputedAtUtc",
                table: "TaskEvaluations");

            migrationBuilder.DropColumn(
                name: "DisputedByUserId",
                table: "TaskEvaluations");

            migrationBuilder.DropColumn(
                name: "LevelOverride",
                table: "TaskEvaluations");

            migrationBuilder.DropColumn(
                name: "OriginalScore",
                table: "TaskEvaluations");

            migrationBuilder.DropColumn(
                name: "RevisedAtUtc",
                table: "TaskEvaluations");

            migrationBuilder.DropColumn(
                name: "RevisedReason",
                table: "TaskEvaluations");
        }
    }
}
