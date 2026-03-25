using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProjectId { get; set; }

    [Required, MaxLength(250)]
    public string Title { get; set; } = default!;
    public string? Description { get; set; }

    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public DateTime? DueDateUtc { get; set; }

    public TaskStatus Status { get; set; } = TaskStatus.Unassigned;

    public string CreatedByUserId { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

