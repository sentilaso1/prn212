using WorkFlowPro.Data;

namespace WorkFlowPro.ViewModels;

public sealed record WorkspaceMemberRoleRowVm(
    string UserId,
    string DisplayName,
    string Email,
    string? AvatarUrl,
    WorkspaceMemberRole Role,
    string? SubRole,
    bool CanChangeWorkspaceRole);

public sealed record RoleManagementResult(bool Success, string? ErrorMessage = null);
