using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class TaskEvaluation
{
    public const int DisputeWindowHours = 24;

    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskId { get; set; }
    public string PmUserId { get; set; } = default!;

    /// <summary>Current score (may change after one revise).</summary>
    public int Score { get; set; }

    /// <summary>Score at initial submit (before revise).</summary>
    public int OriginalScore { get; set; }

    [MaxLength(2000)]
    public string? Comment { get; set; }

    public DateTime EvaluatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsLocked { get; set; }

    /// <summary>PM-chosen level applied when evaluation is finalized; null = auto from avg score.</summary>
    public MemberLevel? LevelOverride { get; set; }

    [MaxLength(2000)]
    public string? DisputeReason { get; set; }

    public string? DisputedByUserId { get; set; }
    public DateTime? DisputedAtUtc { get; set; }

    public DateTime? RevisedAtUtc { get; set; }

    [MaxLength(2000)]
    public string? RevisedReason { get; set; }
}
