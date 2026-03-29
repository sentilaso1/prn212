using WorkFlowPro.Data;

namespace WorkFlowPro.ViewModels;

public sealed record WorkspaceMemberRoleRowVm(
    string UserId,
    string DisplayName,
    string Email,
    string? AvatarUrl,
    WorkspaceMemberRole Role,
    string? SubRole,
    bool CanChangeWorkspaceRole,
    bool IsActorPlatformAdmin,
    bool ShowAdminRoleChangeForm,
    /// <summary>PM gửi yêu cầu đổi PM↔Member — luôn qua Admin duyệt.</summary>
    bool ShowPmRoleRequestForm,
    bool HasPendingPromoteRequest,
    bool HasPendingDemoteRequest,
    bool HasPendingRemovePmRequest,
    /// <summary>Xóa Member ngay (PM/Admin), có lý do.</summary>
    bool ShowRemoveMemberFromWorkspace,
    /// <summary>PM gửi yêu cầu xóa PM khác — chờ Admin.</summary>
    bool ShowRequestRemovePmFromWorkspace);

public sealed record RoleManagementResult(bool Success, string? ErrorMessage = null);
