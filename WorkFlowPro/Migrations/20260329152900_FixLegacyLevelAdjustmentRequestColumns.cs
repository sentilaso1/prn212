using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkFlowPro.Migrations
{
    /// <inheritdoc />
    public partial class FixLegacyLevelAdjustmentRequestColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mỗi Sql() = một batch (sp_rename an toàn). Điều kiện theo từng tên cột — DB mới từ migration gốc sẽ bỏ qua hết.
            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'FromLevel') IS NOT NULL
                    ALTER TABLE dbo.LevelAdjustmentRequests DROP COLUMN FromLevel;
                """);

            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LevelAdjustmentRequests_WorkspaceId_Status' AND object_id = OBJECT_ID('dbo.LevelAdjustmentRequests'))
                    DROP INDEX IX_LevelAdjustmentRequests_WorkspaceId_Status ON dbo.LevelAdjustmentRequests;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'ProposedByPmUserId') IS NOT NULL
                    EXEC sp_rename N'LevelAdjustmentRequests.ProposedByPmUserId', N'RequestedByUserId', N'COLUMN';
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'ToLevel') IS NOT NULL
                    AND COL_LENGTH('dbo.LevelAdjustmentRequests', 'ProposedLevel') IS NULL
                    EXEC sp_rename N'LevelAdjustmentRequests.ToLevel', N'ProposedLevel', N'COLUMN';
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'Justification') IS NOT NULL
                    AND COL_LENGTH('dbo.LevelAdjustmentRequests', 'Reason') IS NULL
                    EXEC sp_rename N'LevelAdjustmentRequests.Justification', N'Reason', N'COLUMN';
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'ReviewedByAdminUserId') IS NOT NULL
                    AND COL_LENGTH('dbo.LevelAdjustmentRequests', 'ReviewedByAdminId') IS NULL
                    EXEC sp_rename N'LevelAdjustmentRequests.ReviewedByAdminUserId', N'ReviewedByAdminId', N'COLUMN';
                """);

            migrationBuilder.Sql(@"
                IF EXISTS (
                    SELECT 1 FROM sys.columns c
                    INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                    WHERE c.object_id = OBJECT_ID('dbo.LevelAdjustmentRequests')
                      AND c.name = N'Status' AND t.name = N'int')
                BEGIN
                    ALTER TABLE dbo.LevelAdjustmentRequests ADD StatusNew nvarchar(20) NULL;
                END
                ");

            migrationBuilder.Sql(@"
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'StatusNew') IS NOT NULL
                    EXEC sp_executesql N'UPDATE dbo.LevelAdjustmentRequests SET StatusNew = CASE [Status] WHEN 0 THEN N''Pending'' WHEN 1 THEN N''Approved'' WHEN 2 THEN N''Rejected'' ELSE N''Pending'' END';
                ");

            migrationBuilder.Sql("""
                DECLARE @dc sysname;
                SELECT TOP 1 @dc = dc.name FROM sys.default_constraints dc
                INNER JOIN sys.columns col ON dc.parent_object_id = col.object_id AND dc.parent_column_id = col.column_id
                WHERE dc.parent_object_id = OBJECT_ID('dbo.LevelAdjustmentRequests') AND col.name = N'Status';
                IF @dc IS NOT NULL EXEC(N'ALTER TABLE dbo.LevelAdjustmentRequests DROP CONSTRAINT [' + @dc + N']');
                """);

            migrationBuilder.Sql(@"
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'StatusNew') IS NOT NULL
                BEGIN
                    ALTER TABLE dbo.LevelAdjustmentRequests DROP COLUMN Status;
                    EXEC sp_rename N'LevelAdjustmentRequests.StatusNew', N'Status', N'COLUMN';
                    ALTER TABLE dbo.LevelAdjustmentRequests ALTER COLUMN Status nvarchar(20) NOT NULL;
                END
                ");

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'Reason') IS NOT NULL
                    ALTER TABLE dbo.LevelAdjustmentRequests ALTER COLUMN Reason nvarchar(500) NOT NULL;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'TargetUserId') IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_LevelAdjustmentRequests_AspNetUsers_TargetUserId')
                    ALTER TABLE dbo.LevelAdjustmentRequests WITH CHECK ADD CONSTRAINT FK_LevelAdjustmentRequests_AspNetUsers_TargetUserId
                    FOREIGN KEY (TargetUserId) REFERENCES dbo.AspNetUsers(Id) ON DELETE CASCADE;
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'RequestedByUserId') IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_LevelAdjustmentRequests_AspNetUsers_RequestedByUserId')
                    ALTER TABLE dbo.LevelAdjustmentRequests WITH CHECK ADD CONSTRAINT FK_LevelAdjustmentRequests_AspNetUsers_RequestedByUserId
                    FOREIGN KEY (RequestedByUserId) REFERENCES dbo.AspNetUsers(Id);
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'RequestedByUserId') IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LevelAdjustmentRequests_RequestedByUserId' AND object_id = OBJECT_ID('dbo.LevelAdjustmentRequests'))
                    CREATE NONCLUSTERED INDEX IX_LevelAdjustmentRequests_RequestedByUserId ON dbo.LevelAdjustmentRequests(RequestedByUserId);
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'TargetUserId') IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LevelAdjustmentRequests_TargetUserId' AND object_id = OBJECT_ID('dbo.LevelAdjustmentRequests'))
                    CREATE NONCLUSTERED INDEX IX_LevelAdjustmentRequests_TargetUserId ON dbo.LevelAdjustmentRequests(TargetUserId);
                """);

            migrationBuilder.Sql("""
                IF COL_LENGTH('dbo.LevelAdjustmentRequests', 'WorkspaceId') IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_LevelAdjustmentRequests_WorkspaceId' AND object_id = OBJECT_ID('dbo.LevelAdjustmentRequests'))
                    CREATE NONCLUSTERED INDEX IX_LevelAdjustmentRequests_WorkspaceId ON dbo.LevelAdjustmentRequests(WorkspaceId);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
