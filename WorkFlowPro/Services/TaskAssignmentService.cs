using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;

namespace WorkFlowPro.Services;

using TaskStatus = WorkFlowPro.Data.TaskStatus;

public sealed class TaskAssignmentService : ITaskAssignmentService
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly INotificationService _notifications;
    private readonly ILogger<TaskAssignmentService> _logger;
    private readonly Microsoft.AspNetCore.SignalR.IHubContext<TaskHub> _hub;

    public TaskAssignmentService(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService,
        INotificationService notifications,
        ILogger<TaskAssignmentService> logger,
        Microsoft.AspNetCore.SignalR.IHubContext<TaskHub> hub)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
        _notifications = notifications;
        _logger = logger;
        _hub = hub;
    }

    public async Task<AssignmentActionResult> AcceptAsync(
        Guid taskId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Workspace không hợp lệ." };

        if (string.IsNullOrWhiteSpace(actorUserId))
            return new AssignmentActionResult { Success = false, ErrorMessage = "User không hợp lệ." };

        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Authorization: only member (not PM) and must be assignee.
        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId.Value &&
            m.UserId == actorUserId &&
            m.Role == WorkspaceMemberRole.PM, cancellationToken);

        if (isPm)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Chỉ Member được Accept task." };

        var assignment = await _db.TaskAssignments
            .Where(a => a.TaskId == taskId)
            .Where(a => a.AssigneeUserId == actorUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (assignment is null)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Task không tồn tại hoặc không được giao cho bạn." };

        if (assignment.Status != TaskAssignmentStatus.Pending)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Task assignment hiện không ở trạng thái Pending." };

        // Ensure task belongs to current workspace + correct state machine precondition.
        var task = await _db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task is null)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Task không thuộc workspace hiện tại." };

        if (task.Status != TaskStatus.Pending)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Task phải đang ở trạng thái Pending." };

        assignment.Status = TaskAssignmentStatus.Accepted;
        assignment.AcceptedAtUtc = now;

        // UC-05 state machine: Pending -> ToDo
        var oldTaskStatus = task.Status;
        task.Status = TaskStatus.ToDo;
        task.UpdatedAtUtc = now;
        _db.TaskAssignments.Update(assignment);
        _db.Tasks.Update(task);

        // Update member profile workload (+1)
        var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p =>
            p.UserId == actorUserId, cancellationToken);

        profile ??= new MemberProfile { UserId = actorUserId };
        if (_db.Entry(profile).State == EntityState.Detached)
            _db.MemberProfiles.Add(profile);
        profile.CurrentWorkload += 1;

        // Activity log: "Accepted task"
        _db.TaskHistoryEntries.Add(new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actorUserId,
            Action = "Accept task",
            OldValue = oldTaskStatus.ToString(),
            NewValue = task.Status.ToString(),
            TimestampUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // UC-06: broadcast to Kanban board in realtime.
        await BroadcastTaskMovedAsync(task.ProjectId, taskId, task.Status, cancellationToken);

        await TryNotifyPmAsync(workspaceId.Value, task, cancellationToken);

        return new AssignmentActionResult { Success = true };
    }

    public async Task<AssignmentActionResult> RejectAsync(
        Guid taskId,
        string actorUserId,
        string rejectReason,
        CancellationToken cancellationToken = default)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Workspace không hợp lệ." };

        if (string.IsNullOrWhiteSpace(actorUserId))
            return new AssignmentActionResult { Success = false, ErrorMessage = "User không hợp lệ." };

        rejectReason = rejectReason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rejectReason))
            return new AssignmentActionResult { Success = false, ErrorMessage = "Reject reason là bắt buộc." };

        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        // Authorization: only member (not PM) and must be assignee.
        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId.Value &&
            m.UserId == actorUserId &&
            m.Role == WorkspaceMemberRole.PM, cancellationToken);

        if (isPm)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Chỉ Member được Reject task." };

        var assignment = await _db.TaskAssignments
            .Where(a => a.TaskId == taskId)
            .Where(a => a.AssigneeUserId == actorUserId)
            .FirstOrDefaultAsync(cancellationToken);

        if (assignment is null)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Task không tồn tại hoặc không được giao cho bạn." };

        if (assignment.Status != TaskAssignmentStatus.Pending)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Task assignment hiện không ở trạng thái Pending." };

        var task = await _db.Tasks
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);

        if (task is null)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Task không thuộc workspace hiện tại." };

        // UC-05 state machine: Pending -> Unassigned
        var oldTaskStatus = task.Status;

        if (task.Status != TaskStatus.Pending)
            return new AssignmentActionResult { Success = false, ErrorMessage = "Task phải đang ở trạng thái Pending." };

        assignment.Status = TaskAssignmentStatus.Rejected;
        assignment.RejectedAtUtc = now;
        assignment.RejectReason = rejectReason;

        task.Status = TaskStatus.Unassigned;
        task.UpdatedAtUtc = now;
        _db.TaskAssignments.Update(assignment);
        _db.Tasks.Update(task);

        // Update member profile workload (-1) to keep KPI consistent.
        var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p =>
            p.UserId == actorUserId, cancellationToken);
        if (profile is not null && profile.CurrentWorkload > 0)
            profile.CurrentWorkload -= 1;

        _db.TaskHistoryEntries.Add(new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actorUserId,
            Action = "Reject task",
            OldValue = oldTaskStatus.ToString(),
            NewValue = task.Status.ToString(),
            TimestampUtc = now
        });

        // Keep reason in a separate entry to avoid overloading Action.
        if (!string.IsNullOrWhiteSpace(rejectReason))
        {
            var reason = rejectReason.Length > 500 ? rejectReason[..500] : rejectReason;
            _db.TaskHistoryEntries.Add(new TaskHistoryEntry
            {
                TaskId = taskId,
                ActorUserId = actorUserId,
                Action = "Reject reason",
                OldValue = null,
                NewValue = reason,
                TimestampUtc = now
            });
        }

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // UC-06: broadcast to Kanban board in realtime (card will be removed).
        await BroadcastTaskMovedAsync(task.ProjectId, taskId, task.Status, cancellationToken);

        await TryNotifyPmAsync(workspaceId.Value, task, cancellationToken, rejectReason);

        return new AssignmentActionResult { Success = true };
    }

    private async Task BroadcastTaskMovedAsync(
        Guid projectId,
        Guid taskId,
        WorkFlowPro.Data.TaskStatus newStatus,
        CancellationToken cancellationToken)
    {
        TaskMovedPayload payload;
        if (newStatus is TaskStatus.ToDo or TaskStatus.InProgress or TaskStatus.Review or TaskStatus.Done)
        {
            // For displayed statuses, include card data.
            var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
            if (task is null) return;

            var assigneeUserId = await _db.TaskAssignments
                .Where(a => a.TaskId == taskId && a.Status == TaskAssignmentStatus.Accepted)
                .Select(a => a.AssigneeUserId)
                .FirstOrDefaultAsync(cancellationToken);

            string? assigneeName = null;
            string? avatarUrl = null;
            if (!string.IsNullOrWhiteSpace(assigneeUserId))
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == assigneeUserId, cancellationToken);
                assigneeName = user?.DisplayName ?? user?.Email ?? user?.UserName;
                avatarUrl = user?.AvatarUrl;
            }

            payload = new TaskMovedPayload(
                taskId: taskId,
                newStatus: newStatus.ToString(),
                card: new TaskCardPayload(
                    taskId: task.Id,
                    title: task.Title,
                    projectId: task.ProjectId,
                    priority: task.Priority.ToString(),
                    dueDateUtc: task.DueDateUtc,
                    status: task.Status.ToString(),
                    assigneeUserId: assigneeUserId ?? string.Empty,
                    assigneeDisplayName: assigneeName ?? string.Empty,
                    assigneeAvatarUrl: avatarUrl));
        }
        else
        {
            payload = new TaskMovedPayload(taskId, newStatus.ToString(), card: null);
        }

        await _hub.Clients.Group(projectId.ToString("D")).SendAsync(
            "taskMoved",
            payload,
            cancellationToken);

        await _hub.Clients.Group(projectId.ToString("D")).SendAsync(
            "TaskUpdated",
            payload,
            cancellationToken);
    }

    private async Task TryNotifyPmAsync(
        Guid workspaceId,
        TaskItem task,
        CancellationToken cancellationToken,
        string? rejectReason = null)
    {
        // Notification to all PMs in this workspace (if any).
        var pmUserIds = await _db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM)
            .Select(m => m.UserId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (pmUserIds.Count == 0)
            return;

        var message = rejectReason is null
            ? $"Task \"{task.Title}\" đã được chấp nhận."
            : $"Task \"{task.Title}\" bị từ chối. Lý do: {rejectReason}";

        var type = rejectReason is null
            ? NotificationType.TaskAccepted
            : NotificationType.TaskRejected;

        // Resolve projectId for notification redirect.
        var projectId = task.ProjectId;

        foreach (var pmUserId in pmUserIds)
        {
            try
            {
                await _notifications.CreateAndPushAsync(
                    userId: pmUserId,
                    type: type,
                    message: message,
                    workspaceId: workspaceId,
                    projectId: projectId,
                    taskId: task.Id,
                    redirectUrl: $"/board?projectId={projectId}",
                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                // Do not fail accept/reject if notification is broken.
                _logger.LogError(ex, "Failed to push PM notification for task {TaskId}", task.Id);
            }
        }
    }
}

