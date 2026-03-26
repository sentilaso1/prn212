using Microsoft.EntityFrameworkCore;

namespace WorkFlowPro.Data;

public static class WorkspacePolicies
{
    public const int MaxPmsPerWorkspace = 3;

    public static Task<int> CountPmsAsync(
        WorkFlowProDbContext db,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        db.WorkspaceMembers.CountAsync(
            m => m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM,
            cancellationToken);
}
