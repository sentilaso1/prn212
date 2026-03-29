using Microsoft.EntityFrameworkCore;

namespace WorkFlowPro.Data;

public static class WorkspacePolicies
{
    public const int MaxPmsPerWorkspace = 3;

    /// <summary>SubRole chuẩn cho Member (UC-03 invite / Roles).</summary>
    public static readonly string[] AllowedMemberSubRoles = ["BA", "DEV", "Designer", "QA"];

    public static bool IsAllowedMemberSubRole(string? subRole)
    {
        if (string.IsNullOrWhiteSpace(subRole))
            return false;
        var t = subRole.Trim();
        return AllowedMemberSubRoles.Contains(t, StringComparer.OrdinalIgnoreCase);
    }

    public static Task<int> CountPmsAsync(
        WorkFlowProDbContext db,
        Guid workspaceId,
        CancellationToken cancellationToken = default) =>
        db.WorkspaceMembers.CountAsync(
            m => m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM,
            cancellationToken);
}
