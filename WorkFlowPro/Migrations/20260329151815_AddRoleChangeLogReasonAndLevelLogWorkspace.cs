using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkFlowPro.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleChangeLogReasonAndLevelLogWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reason",
                table: "RoleChangeLogs",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkspaceId",
                table: "LevelChangeLogs",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LevelChangeLogs_WorkspaceId",
                table: "LevelChangeLogs",
                column: "WorkspaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_LevelChangeLogs_Workspaces_WorkspaceId",
                table: "LevelChangeLogs",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LevelChangeLogs_Workspaces_WorkspaceId",
                table: "LevelChangeLogs");

            migrationBuilder.DropIndex(
                name: "IX_LevelChangeLogs_WorkspaceId",
                table: "LevelChangeLogs");

            migrationBuilder.DropColumn(
                name: "Reason",
                table: "RoleChangeLogs");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "LevelChangeLogs");
        }
    }
}
