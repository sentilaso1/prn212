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

    /// <summary>
    /// PM: cập nhật Level + SubRole cho member trong workspace. Ghi LevelChangeLog khi Level đổi.
    /// </summary>
    Task<MemberProfileResult> UpdateLevelAsync(
        Guid workspaceId,
        string pmUserId,
        string targetUserId,
        MemberLevel newLevel,
        string? subRole,
        CancellationToken cancellationToken = default);
}
