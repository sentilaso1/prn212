using WorkFlowPro.Data;

namespace WorkFlowPro.ViewModels;

using TaskItemStatus = WorkFlowPro.Data.TaskStatus;

public sealed class TaskCardVm
{
    public TaskCardVm(
        Guid taskId,
        string title,
        Guid projectId,
        TaskPriority priority,
        DateTime? dueDateUtc,
        TaskItemStatus status,
        string assigneeUserId,
        string assigneeDisplayName,
        string? assigneeAvatarUrl,
        bool isOverdue,
        bool canDrag,
        DateTime createdAtUtc)
    {
        TaskId = taskId;
        Title = title;
        ProjectId = projectId;
        Priority = priority;
        DueDateUtc = dueDateUtc;
        Status = status;
        AssigneeUserId = assigneeUserId;
        AssigneeDisplayName = assigneeDisplayName;
        AssigneeAvatarUrl = assigneeAvatarUrl;
        IsOverdue = isOverdue;
        CanDrag = canDrag;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid TaskId { get; }
    public string Title { get; }
    public Guid ProjectId { get; }
    public TaskPriority Priority { get; }
    public DateTime? DueDateUtc { get; }
    public TaskItemStatus Status { get; }
    public string AssigneeUserId { get; }
    public string AssigneeDisplayName { get; }
    public string? AssigneeAvatarUrl { get; }
    public bool IsOverdue { get; }
    public bool CanDrag { get; }
    public DateTime CreatedAtUtc { get; }
}
