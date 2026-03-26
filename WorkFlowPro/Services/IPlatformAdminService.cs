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

    Task<IReadOnlyList<AdminWorkspaceListItemVm>> ListAllWorkspacesAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminPmRowVm>> ListPmsInWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);
}

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
