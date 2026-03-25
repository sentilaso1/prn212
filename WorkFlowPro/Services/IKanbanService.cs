using WorkFlowPro.Data;
using TaskStatus = WorkFlowPro.Data.TaskStatus;

namespace WorkFlowPro.Services;

public interface IKanbanService
{
    Task<MoveTaskServiceResult> UpdateTaskStatusAsync(
        Guid taskId,
        TaskStatus newStatus,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);
}

public sealed class MoveTaskServiceResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

