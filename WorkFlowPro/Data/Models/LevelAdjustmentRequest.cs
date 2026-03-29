using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

/// <summary>UC-10 / UC-13 §2: PM đề xuất đổi Level cho member — Admin duyệt mới áp dụng.</summary>
public sealed class LevelAdjustmentRequest
{
    public int Id { get; set; }

    public Guid WorkspaceId { get; set; }

    [Required, MaxLength(450)]
    public string TargetUserId { get; set; } = default!;

    [Required, MaxLength(450)]
    public string ProposedByPmUserId { get; set; } = default!;

    public MemberLevel FromLevel { get; set; }

    public MemberLevel ToLevel { get; set; }

    [Required, MaxLength(2000)]
    public string Justification { get; set; } = default!;

    public LevelAdjustmentRequestStatus Status { get; set; } = LevelAdjustmentRequestStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ReviewedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ReviewedByAdminUserId { get; set; }

    [MaxLength(500)]
    public string? AdminNote { get; set; }

    public Workspace? Workspace { get; set; }
}
