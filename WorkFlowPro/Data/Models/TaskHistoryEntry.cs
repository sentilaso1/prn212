using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class TaskHistoryEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskId { get; set; }
    public string ActorUserId { get; set; } = default!;

    // Human readable action for UC-18
    [MaxLength(500)]
    public string Action { get; set; } = default!;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
}

