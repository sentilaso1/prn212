using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public interface ITaskAssignmentService
{
    Task<AssignmentActionResult> AcceptAsync(
        Guid taskId,
        string actorUserId,
        CancellationToken cancellationToken = default);

    Task<AssignmentActionResult> RejectAsync(
        Guid taskId,
        string actorUserId,
        string rejectReason,
        CancellationToken cancellationToken = default);
}

public sealed class AssignmentActionResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

