namespace WorkFlowPro.Services;

using WorkFlowPro.Data;

public record LevelAdjustmentResult(bool Success, string? ErrorMessage = null);

public interface ILevelAdjustmentService
{
    /// <summary>
    /// PM đề xuất thay đổi level cho Member.
    /// </summary>
    Task<LevelAdjustmentResult> ProposeLevelChangeAsync(
        Guid workspaceId,
        string targetUserId,
        string requestedByUserId,
        MemberLevel proposedLevel,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Kiểm tra xem user có đang có yêu cầu thay đổi level nào đang chờ duyệt không.
    /// </summary>
    Task<bool> HasPendingRequestAsync(Guid workspaceId, string targetUserId, CancellationToken cancellationToken = default);
}
