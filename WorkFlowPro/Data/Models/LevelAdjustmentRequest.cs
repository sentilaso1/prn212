using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

/// <summary>
/// UC-10: PM đề xuất thay đổi Level cho Member. 
/// Admin duyệt tại UC-13.
/// </summary>
public sealed class LevelAdjustmentRequest
{
    public int Id { get; set; }

    public Guid WorkspaceId { get; set; }

    [Required, MaxLength(450)]
    public string TargetUserId { get; set; } = default!;

    [Required, MaxLength(450)]
    public string RequestedByUserId { get; set; } = default!;

    public MemberLevel ProposedLevel { get; set; }

    public LevelAdjustmentRequestStatus Status { get; set; } = LevelAdjustmentRequestStatus.Pending;

    [Required, MaxLength(500)]
    public string Reason { get; set; } = default!;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ReviewedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ReviewedByAdminId { get; set; }

    [MaxLength(500)]
    public string? AdminNote { get; set; }

    public Workspace? Workspace { get; set; }
}
