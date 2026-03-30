using WorkFlowPro.Auth;
using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public interface IPlatformAdminService
{
    Task<IReadOnlyList<PendingPmRegistrationVm>> GetPendingPmRegistrationsAsync(
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> ApprovePmRegistrationAsync(
        string adminUserId,
        string targetUserId,
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> RejectPmRegistrationAsync(
        string adminUserId,
        string targetUserId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>UC-13 Section 2 — đề xuất đổi level (UC-10) chờ Admin.</summary>
    Task<IReadOnlyList<PendingLevelAdjustmentVm>> GetPendingLevelAdjustmentsAsync(
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> ApproveLevelAdjustmentAsync(
        string adminUserId,
        int requestId,
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> RejectLevelAdjustmentAsync(
        string adminUserId,
        int requestId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WorkspaceRoleRequestListVm>> GetPendingWorkspaceRoleRequestsAsync(
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> ApproveWorkspaceRoleRequestAsync(
        string adminUserId,
        int requestId,
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> RejectWorkspaceRoleRequestAsync(
        string adminUserId,
        int requestId,
        string? adminNote,
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> DemotePmDirectAsync(
        string adminUserId,
        Guid workspaceId,
        string targetUserId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> SubmitPromoteToPmRequestAsync(
        string pmUserId,
        Guid workspaceId,
        string targetUserId,
        string? reason,
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> SubmitDemotePmRequestAsync(
        string pmUserId,
        Guid workspaceId,
        string targetUserId,
        string? reason,
        CancellationToken cancellationToken = default);

    /// <summary>PM yêu cầu xóa PM khác khỏi đơn vị — lý do bắt buộc, chờ Admin duyệt.</summary>
    Task<AdminActionResult> SubmitRemovePmFromWorkspaceRequestAsync(
        string pmUserId,
        Guid workspaceId,
        string targetUserId,
        string reason,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminWorkspaceListItemVm>> ListAllWorkspacesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminPmRowVm>> ListPmsInWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    // UC-11 & UC-14: Project Approval
    Task<IReadOnlyList<PendingProjectVm>> GetPendingProjectsAsync(
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> ApproveProjectAsync(
        string adminUserId,
        Guid projectId,
        CancellationToken cancellationToken = default);

    Task<AdminActionResult> RejectProjectAsync(
        string adminUserId,
        Guid projectId,
        string? reason,
        CancellationToken cancellationToken = default);
}

public sealed record PendingProjectVm(
    Guid Id,
    Guid WorkspaceId,
    string WorkspaceName,
    string ProjectName,
    string? Description,
    string OwnerUserId,
    string OwnerDisplayName,
    DateTime CreatedAtUtc);

public sealed record PendingPmRegistrationVm(
    string UserId,
    string Email,
    string? DisplayName,
    string? PendingWorkspaceName,
    DateTimeOffset? LockoutEnd);

public sealed record WorkspaceRoleRequestListVm(
    int Id,
    Guid WorkspaceId,
    string WorkspaceName,
    WorkspaceRoleRequestKind Kind,
    string TargetUserId,
    string TargetDisplay,
    string RequestedByUserId,
    string RequesterDisplay,
    string? Reason,
    DateTime CreatedAtUtc);

public sealed record AdminActionResult(bool Success, string? ErrorMessage = null);

public sealed record AdminWorkspaceListItemVm(Guid Id, string Name);

public sealed record AdminPmRowVm(string UserId, string DisplayName, string Email);

public sealed record PendingLevelAdjustmentVm(
    int Id,
    Guid WorkspaceId,
    string WorkspaceName,
    string TargetUserId,
    string TargetDisplayName,
    MemberLevel CurrentLevel,
    MemberLevel ProposedLevel,
    string RequestedByUserId,
    string RequesterDisplayName,
    string Reason,
    decimal CompletionRate,
    decimal AvgScore,
    DateTime CreatedAtUtc);
