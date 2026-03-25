namespace WorkFlowPro.Data;

public sealed class TaskAssignment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskId { get; set; }
    public string AssigneeUserId { get; set; } = default!;
    public TaskAssignmentStatus Status { get; set; } = TaskAssignmentStatus.Pending;

    public string? RejectReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RejectedAtUtc { get; set; }
}

