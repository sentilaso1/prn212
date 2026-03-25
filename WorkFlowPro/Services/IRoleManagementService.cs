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
}
