using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkFlowPro.Migrations
{
    /// <inheritdoc />
    public partial class AdminAccountAndRoleRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent cho SQL Server: DB có thể đã có AcceptUrl/CreatedAtUtc/Status (chạy migration dở hoặc sync tay).
            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.WorkspaceInviteTokens', N'AcceptUrl') IS NULL
    ALTER TABLE [WorkspaceInviteTokens] ADD [AcceptUrl] nvarchar(500) NULL;

IF COL_LENGTH(N'dbo.WorkspaceInviteTokens', N'CreatedAtUtc') IS NULL
BEGIN
    ALTER TABLE [WorkspaceInviteTokens] ADD [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_WorkspaceInviteTokens_CreatedAtUtc] DEFAULT (GETUTCDATE());
END

IF COL_LENGTH(N'dbo.WorkspaceInviteTokens', N'Status') IS NULL
BEGIN
    ALTER TABLE [WorkspaceInviteTokens] ADD [Status] int NOT NULL CONSTRAINT [DF_WorkspaceInviteTokens_Status] DEFAULT (1);
END

IF COL_LENGTH(N'dbo.AspNetUsers', N'AccountStatus') IS NULL
BEGIN
    ALTER TABLE [AspNetUsers] ADD [AccountStatus] int NOT NULL CONSTRAINT [DF_AspNetUsers_AccountStatus] DEFAULT (1);
END

IF COL_LENGTH(N'dbo.AspNetUsers', N'AwaitingPmWorkspaceApproval') IS NULL
BEGIN
    ALTER TABLE [AspNetUsers] ADD [AwaitingPmWorkspaceApproval] bit NOT NULL CONSTRAINT [DF_AspNetUsers_AwaitingPmWorkspaceApproval] DEFAULT (0);
END

IF COL_LENGTH(N'dbo.AspNetUsers', N'PendingWorkspaceName') IS NULL
    ALTER TABLE [AspNetUsers] ADD [PendingWorkspaceName] nvarchar(200) NULL;
");

            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[WorkspaceRoleChangeRequests]', N'U') IS NULL
BEGIN
    CREATE TABLE [WorkspaceRoleChangeRequests] (
        [Id] int NOT NULL IDENTITY(1,1),
        [WorkspaceId] uniqueidentifier NOT NULL,
        [TargetUserId] nvarchar(450) NOT NULL,
        [RequestedByUserId] nvarchar(450) NOT NULL,
        [Kind] int NOT NULL,
        [Status] int NOT NULL,
        [Reason] nvarchar(500) NULL,
        [CreatedAtUtc] datetime2 NOT NULL CONSTRAINT [DF_WorkspaceRoleChangeRequests_CreatedAtUtc] DEFAULT (GETUTCDATE()),
        [ReviewedAtUtc] datetime2 NULL,
        [ReviewedByAdminId] nvarchar(450) NULL,
        [AdminNote] nvarchar(500) NULL,
        CONSTRAINT [PK_WorkspaceRoleChangeRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkspaceRoleChangeRequests_Workspaces_WorkspaceId] FOREIGN KEY ([WorkspaceId]) REFERENCES [Workspaces] ([Id]) ON DELETE CASCADE
    );
END
");

            migrationBuilder.Sql(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_WorkspaceRoleChangeRequests_WorkspaceId_Status' AND object_id = OBJECT_ID(N'[dbo].[WorkspaceRoleChangeRequests]'))
    CREATE INDEX [IX_WorkspaceRoleChangeRequests_WorkspaceId_Status] ON [WorkspaceRoleChangeRequests] ([WorkspaceId], [Status]);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'[dbo].[WorkspaceRoleChangeRequests]', N'U') IS NOT NULL
BEGIN
    DROP TABLE [WorkspaceRoleChangeRequests];
END
");

            migrationBuilder.Sql(@"
IF COL_LENGTH(N'dbo.WorkspaceInviteTokens', N'AcceptUrl') IS NOT NULL
    ALTER TABLE [WorkspaceInviteTokens] DROP COLUMN [AcceptUrl];
IF COL_LENGTH(N'dbo.WorkspaceInviteTokens', N'CreatedAtUtc') IS NOT NULL
BEGIN
    ALTER TABLE [WorkspaceInviteTokens] DROP CONSTRAINT [DF_WorkspaceInviteTokens_CreatedAtUtc];
    ALTER TABLE [WorkspaceInviteTokens] DROP COLUMN [CreatedAtUtc];
END
IF COL_LENGTH(N'dbo.WorkspaceInviteTokens', N'Status') IS NOT NULL
BEGIN
    ALTER TABLE [WorkspaceInviteTokens] DROP CONSTRAINT [DF_WorkspaceInviteTokens_Status];
    ALTER TABLE [WorkspaceInviteTokens] DROP COLUMN [Status];
END
IF COL_LENGTH(N'dbo.AspNetUsers', N'AccountStatus') IS NOT NULL
BEGIN
    ALTER TABLE [AspNetUsers] DROP CONSTRAINT [DF_AspNetUsers_AccountStatus];
    ALTER TABLE [AspNetUsers] DROP COLUMN [AccountStatus];
END
IF COL_LENGTH(N'dbo.AspNetUsers', N'AwaitingPmWorkspaceApproval') IS NOT NULL
BEGIN
    ALTER TABLE [AspNetUsers] DROP CONSTRAINT [DF_AspNetUsers_AwaitingPmWorkspaceApproval];
    ALTER TABLE [AspNetUsers] DROP COLUMN [AwaitingPmWorkspaceApproval];
END
IF COL_LENGTH(N'dbo.AspNetUsers', N'PendingWorkspaceName') IS NOT NULL
    ALTER TABLE [AspNetUsers] DROP COLUMN [PendingWorkspaceName];
");
        }
    }
}
