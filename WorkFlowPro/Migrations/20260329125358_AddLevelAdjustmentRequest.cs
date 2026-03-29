using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkFlowPro.Migrations
{
    /// <inheritdoc />
    public partial class AddLevelAdjustmentRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LevelAdjustmentRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WorkspaceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TargetUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    RequestedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProposedLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ReviewedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedByAdminId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    AdminNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LevelAdjustmentRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LevelAdjustmentRequests_AspNetUsers_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LevelAdjustmentRequests_AspNetUsers_TargetUserId",
                        column: x => x.TargetUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LevelAdjustmentRequests_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LevelAdjustmentRequests_RequestedByUserId",
                table: "LevelAdjustmentRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelAdjustmentRequests_TargetUserId",
                table: "LevelAdjustmentRequests",
                column: "TargetUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LevelAdjustmentRequests_WorkspaceId",
                table: "LevelAdjustmentRequests",
                column: "WorkspaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LevelAdjustmentRequests");
        }
    }
}
