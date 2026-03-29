using WorkFlowPro.Data;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Services;

public interface IRoleManagementService
{
    Task<IReadOnlyList<WorkspaceMemberRoleRowVm>> GetWorkspaceMembersAsync(
        Guid workspaceId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<RoleManagementResult> ChangeRoleAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        WorkspaceMemberRole newRole,
        CancellationToken cancellationToken = default);

    Task<RoleManagementResult> ChangeSubRoleAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        string? subRole,
        CancellationToken cancellationToken = default);

    /// <summary>PM/Admin xóa Member ngay — lý do bắt buộc; task đang giao → Unassigned.</summary>
    Task<RoleManagementResult> RemoveMemberFromWorkspaceAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>Gỡ user khỏi workspace (task, thông báo). Dùng khi Admin duyệt xóa PM; <paramref name="excludeWorkspaceRoleRequestId"/> giữ bản ghi yêu cầu đang duyệt.</summary>
    Task<RoleManagementResult> ExecuteRemoveUserFromWorkspaceAsync(
        Guid workspaceId,
        string targetUserId,
        string reason,
        string actionRecordedAsUserId,
        int? excludeWorkspaceRoleRequestId,
        CancellationToken cancellationToken = default);
}
