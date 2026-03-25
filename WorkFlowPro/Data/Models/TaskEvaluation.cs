using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class TaskEvaluation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskId { get; set; }
    public string PmUserId { get; set; } = default!;

    // 1..10
    public int Score { get; set; }
    [MaxLength(2000)]
    public string? Comment { get; set; }
    public DateTime EvaluatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsLocked { get; set; } = false;
}

