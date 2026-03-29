using Microsoft.AspNetCore.Http;
using WorkFlowPro.Data;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Services;

public interface IMemberProfileService
{
    Task<MemberProfilePageVm?> GetProfilePageAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        CancellationToken cancellationToken = default);

    Task<MemberProfileResult> UpdateProfileAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        string fullName,
        IFormFile? avatarFile,
        CancellationToken cancellationToken = default);

    /// <summary>PM: chỉnh SubRole member (áp dụng ngay). Level đổi qua đề xuất Admin (UC-10/13).</summary>
    Task<MemberProfileResult> UpdateMemberSubRoleOnlyAsync(
        Guid workspaceId,
        string pmUserId,
        string targetUserId,
        string? subRole,
        CancellationToken cancellationToken = default);

    /// <summary>PM: gửi đề xuất đổi Level — chờ Admin duyệt.</summary>
    Task<MemberProfileResult> SubmitLevelAdjustmentProposalAsync(
        Guid workspaceId,
        string pmUserId,
        string targetUserId,
        MemberLevel proposedLevel,
        string justification,
        CancellationToken cancellationToken = default);

    /// <summary>Admin duyệt đề xuất Level — ghi log với PM đề xuất.</summary>
    Task<MemberProfileResult> ApplyApprovedLevelAdjustmentAsync(
        Guid workspaceId,
        string targetUserId,
        MemberLevel newLevel,
        string proposedByPmUserIdForLog,
        CancellationToken cancellationToken = default);
}
