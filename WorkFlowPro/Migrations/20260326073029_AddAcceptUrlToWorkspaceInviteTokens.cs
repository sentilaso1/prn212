using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkFlowPro.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptUrlToWorkspaceInviteTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AcceptUrl",
                table: "WorkspaceInviteTokens",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AcceptUrl",
                table: "WorkspaceInviteTokens");
        }
    }
}
