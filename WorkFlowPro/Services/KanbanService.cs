using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.SignalR;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;

namespace WorkFlowPro.Services;

using TaskStatus = WorkFlowPro.Data.TaskStatus;

public sealed class KanbanService : IKanbanService
{
    private readonly WorkFlowProDbContext _db;
    private readonly IHubContext<TaskHub> _hub;
    private readonly INotificationService _notifications;
    private readonly ILogger<KanbanService> _logger;

    public KanbanService(
        WorkFlowProDbContext db,
        IHubContext<TaskHub> hub,
        INotificationService notifications,
        ILogger<KanbanService> logger)
    {
        _db = db;
        _hub = hub;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<MoveTaskServiceResult> UpdateTaskStatusAsync(
        Guid taskId,
        TaskStatus newStatus,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (taskId == Guid.Empty)
            return new MoveTaskServiceResult { Success = false, ErrorMessage = "taskId không hợp lệ." };

        if (string.IsNullOrWhiteSpace(actorUserId))
            return new MoveTaskServiceResult { Success = false, ErrorMessage = "actorUserId không hợp lệ." };

        if (!Enum.IsDefined<TaskStatus>(newStatus))
            return new MoveTaskServiceResult { Success = false, ErrorMessage = "newStatus không hợp lệ." };

        var task = await _db.Tasks.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return new MoveTaskServiceResult { Success = false, ErrorMessage = "Task không tồn tại." };

        var project = await _db.Projects.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.Id == task.ProjectId && p.WorkspaceId == workspaceId, cancellationToken);

        if (project is null)
            return new MoveTaskServiceResult { Success = false, ErrorMessage = "Task không thuộc workspace hiện tại." };

        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId && m.UserId == actorUserId && m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        var memberAcceptedAssignment = await _db.TaskAssignments.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a =>
            a.TaskId == taskId &&
            a.AssigneeUserId == actorUserId &&
            a.Status == TaskAssignmentStatus.Accepted, cancellationToken);

        if (!isPm && memberAcceptedAssignment is null)
            return new MoveTaskServiceResult { Success = false, ErrorMessage = "Chỉ được kéo task đã nhận của bạn." };

        var oldStatus = task.Status;

        if (oldStatus == newStatus)
            return new MoveTaskServiceResult { Success = true };

        var allowed = oldStatus switch
        {
            TaskStatus.ToDo => newStatus is TaskStatus.InProgress or TaskStatus.Review or TaskStatus.Done,
            TaskStatus.InProgress => newStatus is TaskStatus.Review or TaskStatus.Done or TaskStatus.ToDo,
            TaskStatus.Review => newStatus is TaskStatus.Done or TaskStatus.InProgress,
            TaskStatus.Done => newStatus is TaskStatus.ToDo or TaskStatus.InProgress or TaskStatus.Review,
            _ => newStatus is TaskStatus.Done
        };

        if (!allowed)
            return new MoveTaskServiceResult { Success = false, ErrorMessage = $"Không thể chuyển {oldStatus} -> {newStatus}." };

        task.Status = newStatus;
        task.UpdatedAtUtc = now;
        _db.Tasks.Update(task);

        if (newStatus == TaskStatus.Done)
        {
            var acceptedAssignment = await _db.TaskAssignments.IgnoreQueryFilters()
                .FirstOrDefaultAsync(a =>
                a.TaskId == taskId && a.Status == TaskAssignmentStatus.Accepted, cancellationToken);

            if (acceptedAssignment is not null)
            {
                var profile = await _db.MemberProfiles.FirstOrDefaultAsync(
                    p => p.UserId == acceptedAssignment.AssigneeUserId,
                    cancellationToken);

                if (profile is not null && profile.CurrentWorkload > 0)
                    profile.CurrentWorkload -= 1;
            }
        }

        _db.TaskHistoryEntries.Add(new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actorUserId,
            Action = "Moved task",
            OldValue = oldStatus.ToString(),
            NewValue = newStatus.ToString(),
            TimestampUtc = now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Broadcast updated status to all clients in this project group.
        try
        {
            var payload = await BuildTaskMovedPayloadAsync(taskId, newStatus, cancellationToken);
            await _hub.Clients.Group(project.Id.ToString("D")).SendAsync(
                "taskMoved",
                payload,
                cancellationToken);

            // Extra event name for future use.
            await _hub.Clients.Group(project.Id.ToString("D")).SendAsync(
                "TaskUpdated",
                payload,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast taskMoved for {TaskId}", taskId);
        }

        await NotifyKanbanMoveAsync(
            taskId,
            task,
            project,
            newStatus,
            actorUserId,
            workspaceId,
            cancellationToken);

        return new MoveTaskServiceResult { Success = true };
    }

    private async Task NotifyKanbanMoveAsync(
        Guid taskId,
        TaskItem task,
        Project project,
        TaskStatus newStatus,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var actor = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == actorUserId, cancellationToken);
            var actorName = actor?.DisplayName ?? actor?.Email ?? actor?.UserName ?? actorUserId;

            var recipientIds = new HashSet<string>(StringComparer.Ordinal);

            var pms = await _db.WorkspaceMembers
                .AsNoTracking()
                .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM)
                .Select(m => m.UserId)
                .ToListAsync(cancellationToken);
            foreach (var p in pms)
            {
                if (p != actorUserId)
                    recipientIds.Add(p);
            }

            var assigneeId = await _db.TaskAssignments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(a => a.TaskId == taskId && a.Status == TaskAssignmentStatus.Accepted)
                .Select(a => a.AssigneeUserId)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrEmpty(assigneeId) && assigneeId != actorUserId)
                recipientIds.Add(assigneeId);

            if (task.CreatedByUserId != actorUserId)
                recipientIds.Add(task.CreatedByUserId);

            var msg =
                $"Task \"{task.Title}\" đã chuyển sang {newStatus} bởi {actorName}.";
            var redirect = $"/Tasks/Details/{taskId}?workspaceId={workspaceId:D}";

            foreach (var uid in recipientIds)
            {
                await _notifications.CreateAndPushAsync(
                    uid,
                    NotificationType.TaskKanbanMoved,
                    msg,
                    workspaceId: workspaceId,
                    projectId: task.ProjectId,
                    taskId: taskId,
                    redirectUrl: redirect,
                    cancellationToken: cancellationToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UC-11: failed to notify kanban move for task {TaskId}", taskId);
        }
    }

    private async Task<TaskMovedPayload> BuildTaskMovedPayloadAsync(
        Guid taskId,
        TaskStatus newStatus,
        CancellationToken cancellationToken)
    {
        // We include card data only for statuses displayed on board.
        var displayStatuses = new[]
        {
            TaskStatus.ToDo,
            TaskStatus.InProgress,
            TaskStatus.Review,
            TaskStatus.Done
        };

        TaskCardPayload? card = null;
        if (displayStatuses.Contains(newStatus))
        {
            var task = await _db.Tasks.IgnoreQueryFilters()
                .FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
            var assignee = await _db.TaskAssignments.IgnoreQueryFilters()
                .Where(a => a.TaskId == taskId && a.Status == TaskAssignmentStatus.Accepted)
                .Select(a => a.AssigneeUserId)
                .FirstOrDefaultAsync(cancellationToken);

            string? assigneeName = null;
            string? avatarUrl = null;
            if (!string.IsNullOrWhiteSpace(assignee))
            {
                var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == assignee, cancellationToken);
                assigneeName = user?.DisplayName ?? user?.Email ?? user?.UserName;
                avatarUrl = user?.AvatarUrl;
            }

            if (task is not null)
            {
                card = new TaskCardPayload(
                    taskId: task.Id,
                    title: task.Title,
                    projectId: task.ProjectId,
                    priority: task.Priority.ToString(),
                    dueDateUtc: task.DueDateUtc,
                    status: task.Status.ToString(),
                    assigneeUserId: assignee ?? string.Empty,
                    assigneeDisplayName: assigneeName ?? string.Empty,
                    assigneeAvatarUrl: avatarUrl);
            }
        }

        return new TaskMovedPayload(
            taskId: taskId,
            newStatus: newStatus.ToString(),
            card: card);
    }
}

public sealed record TaskMovedPayload(
    Guid taskId,
    string newStatus,
    TaskCardPayload? card);

public sealed record TaskCardPayload(
    Guid taskId,
    string title,
    Guid projectId,
    string priority,
    DateTime? dueDateUtc,
    string status,
    string assigneeUserId,
    string assigneeDisplayName,
    string? assigneeAvatarUrl);

