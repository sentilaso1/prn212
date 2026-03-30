using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

/// <summary>UC-15: Nhật ký hành động Admin/PM (append-only).</summary>
public sealed class AdminAuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(450)]
    public string ActorUserId { get; set; } = default!;

    [Required, MaxLength(80)]
    public string ActionType { get; set; } = default!;

    /// <summary>Mô tả mục tiêu (user / project / workspace) — hiển thị nhanh.</summary>
    [Required, MaxLength(500)]
    public string TargetSummary { get; set; } = default!;

    [MaxLength(450)]
    public string? TargetUserId { get; set; }

    public Guid? TargetProjectId { get; set; }

    public Guid? WorkspaceId { get; set; }

    [MaxLength(2000)]
    public string? Notes { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}
