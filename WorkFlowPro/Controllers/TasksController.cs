using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Extensions;
using WorkFlowPro.Hubs;
using WorkFlowPro.Services;
using WorkFlowPro.ViewModels;
using TaskStatus = WorkFlowPro.Data.TaskStatus;

namespace WorkFlowPro.Controllers;

[ApiController]
[Route("api")]
public sealed class TasksController : ControllerBase
{
    private readonly WorkFlowProDbContext _db;
    private readonly IHubContext<TaskHub> _hub;
    private readonly ITaskHistoryService _history;
    private readonly INotificationService _notifications;
    private readonly ITaskService _taskService;

    public TasksController(
        WorkFlowProDbContext db,
        IHubContext<TaskHub> hub,
        ITaskHistoryService history,
        INotificationService notifications,
        ITaskService taskService)
    {
        _db = db;
        _hub = hub;
        _history = history;
        _notifications = notifications;
        _taskService = taskService;
    }

    public sealed record CreateTaskRequest(
        string Title,
        string? Description,
        DateTime? DueDateUtc,
        TaskPriority Priority,
        string? AssigneeUserId);

    public sealed record MoveTaskRequest(TaskStatus NewStatus);

    public sealed record AcceptTaskRequest();

    public sealed record RejectTaskRequest(string Reason);

    public sealed record EvaluateTaskRequest(int Score, string? Comment, string? NewLevel = null);

    [Authorize]
    [HttpGet("projects/{projectId:guid}/kanban")]
    public async Task<ActionResult<object>> GetKanban(Guid projectId)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId);
        if (project is null) return NotFound();

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId && m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM);

        // Kanban shows ToDo/InProgress/Review/Done. Pending/Unassigned are handled separately.
        var baseTasksQuery = _db.Tasks.Where(t => t.ProjectId == projectId && t.Status != TaskStatus.Unassigned && t.Status != TaskStatus.Pending);

        IQueryable<TaskItem> visibleTasksQuery = baseTasksQuery;

        if (!isPm)
        {
            visibleTasksQuery = visibleTasksQuery.Where(t =>
                _db.TaskAssignments.Any(a =>
                    a.TaskId == t.Id &&
                    a.AssigneeUserId == userId &&
                    (a.Status == TaskAssignmentStatus.Accepted || a.Status == TaskAssignmentStatus.Pending)));
        }

        var tasks = await visibleTasksQuery
            .OrderByDescending(t => t.UpdatedAtUtc)
            .ToListAsync();

        DateTime now = DateTime.UtcNow;

        object column(string name, TaskItem[] columnTasks) => new
        {
            name,
            tasks = columnTasks.Select(t => new
            {
                id = t.Id,
                title = t.Title,
                description = t.Description,
                priority = t.Priority.ToString(),
                dueDateUtc = t.DueDateUtc,
                status = t.Status.ToString(),
                isOverdue = t.DueDateUtc.HasValue && t.DueDateUtc.Value < now && t.Status != TaskStatus.Done
            })
        };

        var todo = tasks.Where(t => t.Status == TaskStatus.ToDo).ToArray();
        var inProgress = tasks.Where(t => t.Status == TaskStatus.InProgress).ToArray();
        var review = tasks.Where(t => t.Status == TaskStatus.Review).ToArray();
        var done = tasks.Where(t => t.Status == TaskStatus.Done).ToArray();

        return Ok(new
        {
            projectId,
            columns = new[]
            {
                column("To Do", todo),
                column("In Progress", inProgress),
                column("Review", review),
                column("Done", done)
            }
        });
    }

    [Authorize]
    [HttpPost("projects/{projectId:guid}/tasks")]
    public async Task<ActionResult<object>> CreateTask(Guid projectId, [FromBody] CreateTaskRequest request)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId);
        if (project is null) return NotFound();

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId && m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM);
        if (!isPm) return Forbid();

        if (string.IsNullOrWhiteSpace(request.Title))
            return BadRequest("Title is required.");

        var title = request.Title.Trim();

        if (request.AssigneeUserId is not null)
        {
            var assigneeIsMember = await _db.WorkspaceMembers.AnyAsync(m =>
                m.WorkspaceId == workspaceId && m.UserId == request.AssigneeUserId);
            if (!assigneeIsMember)
                return BadRequest("Assignee must be a member of workspace.");
        }

        // UC-04: Top 3 suggestions (simple heuristic)
        var suggestions = await GetTaskSuggestionsAsync(workspaceId, request.AssigneeUserId, cancellationToken: HttpContext.RequestAborted);

        var task = new TaskItem
        {
            ProjectId = projectId,
            Title = title,
            Description = request.Description,
            DueDateUtc = request.DueDateUtc,
            Priority = request.Priority,
            Status = request.AssigneeUserId is null ? TaskStatus.Unassigned : TaskStatus.Pending,
            CreatedByUserId = userId,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        await _history.LogAsync(
            task.Id,
            User,
            action: "Tạo task",
            oldValue: null,
            newValue: request.Title,
            cancellationToken: HttpContext.RequestAborted);

        if (request.AssigneeUserId is not null)
        {
            var assignment = new TaskAssignment
            {
                TaskId = task.Id,
                AssigneeUserId = request.AssigneeUserId,
                Status = TaskAssignmentStatus.Pending
            };
            _db.TaskAssignments.Add(assignment);
            await _db.SaveChangesAsync();

            await _notifications.CreateAndPushAsync(
                userId: request.AssigneeUserId,
                type: NotificationType.TaskAssignedPending,
                message: $"Bạn được giao task \"{task.Title}\".",
                workspaceId: workspaceId,
                projectId: projectId,
                taskId: task.Id,
                // UC-05: Member nhận task qua notification -> mở trang accept/reject.
                redirectUrl: $"/Tasks/AcceptReject/{task.Id}?workspaceId={workspaceId:D}",
                cancellationToken: HttpContext.RequestAborted);
        }

        return Ok(new
        {
            taskId = task.Id,
            status = task.Status.ToString(),
            suggestions
        });
    }

    [Authorize]
    [HttpPost("tasks/{taskId:guid}/accept")]
    public async Task<ActionResult> Accept(Guid taskId, [FromBody] AcceptTaskRequest _)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);

        if (task is null) return NotFound();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId && p.WorkspaceId == workspaceId);
        if (project is null) return Forbid();

        // Idempotent accept: if already accepted, return OK.
        var alreadyAccepted = await _db.TaskAssignments.AnyAsync(a =>
            a.TaskId == taskId &&
            a.AssigneeUserId == userId &&
            a.Status == TaskAssignmentStatus.Accepted &&
            task.Status == TaskStatus.ToDo);
        if (alreadyAccepted) return Ok();

        var assignment = await _db.TaskAssignments.FirstOrDefaultAsync(a =>
            a.TaskId == taskId &&
            a.AssigneeUserId == userId &&
            a.Status == TaskAssignmentStatus.Pending);

        if (assignment is null) return BadRequest("Task is not pending for this user.");

        assignment.Status = TaskAssignmentStatus.Accepted;
        assignment.AcceptedAtUtc = DateTime.UtcNow;

        var oldStatus = task.Status;
        task.Status = TaskStatus.ToDo;
        task.UpdatedAtUtc = DateTime.UtcNow;
        _db.Tasks.Update(task);

        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        await _history.LogAsync(taskId, User, "Accept task", oldValue: oldStatus.ToString(), newValue: task.Status.ToString(), cancellationToken: HttpContext.RequestAborted);

        await _notifications.CreateAndPushAsync(
            userId: project.OwnerUserId,
            type: NotificationType.TaskAccepted,
            message: $"Task \"{task.Title}\" đã được chấp nhận.",
            workspaceId: workspaceId,
            projectId: project.Id,
            taskId: task.Id,
            redirectUrl: $"/board?projectId={project.Id}",
            cancellationToken: HttpContext.RequestAborted);

        var card = await BuildTaskCardAsync(task.Id, HttpContext.RequestAborted);
        await _hub.Clients.Group(project.Id.ToString("D")).SendAsync(
            "taskMoved",
            new { taskId = task.Id, newStatus = task.Status.ToString(), card },
            cancellationToken: HttpContext.RequestAborted);

        return Ok();
    }

    [Authorize]
    [HttpPost("tasks/{taskId:guid}/reject")]
    public async Task<ActionResult> Reject(Guid taskId, [FromBody] RejectTaskRequest request)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null) return NotFound();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == task.ProjectId && p.WorkspaceId == workspaceId);
        if (project is null) return Forbid();

        var assignment = await _db.TaskAssignments
            .Where(a => a.TaskId == taskId && a.AssigneeUserId == userId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync();

        if (assignment is null) return Forbid();
        if (assignment.Status == TaskAssignmentStatus.Rejected && task.Status == TaskStatus.Unassigned)
            return Ok();

        if (assignment.Status != TaskAssignmentStatus.Pending)
            return BadRequest("Task is not pending for this user.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest("Reject reason is required.");

        assignment.Status = TaskAssignmentStatus.Rejected;
        assignment.RejectReason = request.Reason.Trim();
        assignment.RejectedAtUtc = DateTime.UtcNow;

        var oldStatus = task.Status;
        task.Status = TaskStatus.Unassigned;
        task.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        await _history.LogAsync(
            taskId,
            User,
            "Reject task",
            oldValue: oldStatus.ToString(),
            newValue: task.Status.ToString(),
            cancellationToken: HttpContext.RequestAborted);

        await _notifications.CreateAndPushAsync(
            userId: project.OwnerUserId,
            type: NotificationType.TaskRejected,
            message: $"Task \"{task.Title}\" bị từ chối. Lý do: {assignment.RejectReason}",
            workspaceId: workspaceId,
            projectId: project.Id,
            taskId: task.Id,
            redirectUrl: $"/board?projectId={project.Id}",
            cancellationToken: HttpContext.RequestAborted);

        var card = await BuildTaskCardAsync(task.Id, HttpContext.RequestAborted);
        await _hub.Clients.Group(project.Id.ToString("D")).SendAsync(
            "taskMoved",
            new { taskId = task.Id, newStatus = task.Status.ToString(), card },
            cancellationToken: HttpContext.RequestAborted);

        return Ok();
    }

    [Authorize]
    [HttpPost("projects/{projectId:guid}/tasks/{taskId:guid}/move")]
    public async Task<ActionResult> Move(Guid projectId, Guid taskId, [FromBody] MoveTaskRequest request)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId);
        if (project is null) return NotFound();

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId && t.ProjectId == projectId);
        if (task is null) return NotFound();

        // Member can move only their own accepted tasks.
        var assignment = await _db.TaskAssignments
            .Where(a => a.TaskId == taskId && a.AssigneeUserId == userId && a.Status == TaskAssignmentStatus.Accepted)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync();
        if (assignment is null) return Forbid();

        if (task.Status == TaskStatus.Cancelled)
            return BadRequest("Task is cancelled.");

        var allowed = task.Status switch
        {
            TaskStatus.ToDo => request.NewStatus is TaskStatus.InProgress or TaskStatus.Review or TaskStatus.Done,
            TaskStatus.InProgress => request.NewStatus is TaskStatus.Review or TaskStatus.Done,
            TaskStatus.Review => request.NewStatus is TaskStatus.Done or TaskStatus.InProgress,
            _ => request.NewStatus is TaskStatus.Done
        };
        if (!allowed) return BadRequest("Invalid move transition.");

        var oldStatus = task.Status;
        task.Status = request.NewStatus;
        task.UpdatedAtUtc = DateTime.UtcNow;
        _db.Tasks.Update(task);
        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        await _history.LogAsync(
            taskId,
            User,
            $"Đổi status {oldStatus} -> {task.Status}",
            oldValue: oldStatus.ToString(),
            newValue: task.Status.ToString(),
            cancellationToken: HttpContext.RequestAborted);

        var card = await BuildTaskCardAsync(task.Id, HttpContext.RequestAborted);
        await _hub.Clients.Group(projectId.ToString("D")).SendAsync(
            "taskMoved",
            new { taskId = task.Id, newStatus = task.Status.ToString(), card },
            cancellationToken: HttpContext.RequestAborted);

        if (task.Status == TaskStatus.Done)
        {
            var pmIds = await _db.WorkspaceMembers
                .AsNoTracking()
                .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM)
                .Select(m => m.UserId)
                .ToListAsync(HttpContext.RequestAborted);
            var redirect = $"/Tasks/Details/{task.Id}";
            foreach (var pmId in pmIds)
            {
                await _notifications.CreateAndPushAsync(
                    userId: pmId,
                    type: NotificationType.TaskDoneNeedsEvaluation,
                    message: $"Task \"{task.Title}\" needs evaluation.",
                    workspaceId: workspaceId,
                    projectId: projectId,
                    taskId: task.Id,
                    redirectUrl: redirect,
                    cancellationToken: HttpContext.RequestAborted);
            }
        }

        return Ok();
    }

    /// <summary>UC-16: Lọc/sắp xếp Kanban — JSON + lưu Session.</summary>
    [Authorize]
    [HttpPost("projects/{projectId:guid}/tasks/filter-kanban")]
    public async Task<ActionResult<object>> FilterKanban(Guid projectId, [FromBody] TaskFilterCriteria? criteria)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId);
        if (project is null)
            return NotFound();

        var c = criteria ?? TaskFilterCriteria.Default();
        HttpContext.Session.SetString(TaskFilterSession.KeyForProject(projectId), TaskFilterSession.Serialize(c));

        try
        {
            var result = await _taskService.GetFilteredKanbanTasksAsync(
                projectId,
                workspaceId,
                userId,
                c,
                HttpContext.RequestAborted);

            return Ok(ToKanbanFilterResponse(result));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>UC-16: Danh sách task (list view) — JSON + Session.</summary>
    [Authorize]
    [HttpPost("projects/{projectId:guid}/tasks/filter-list")]
    public async Task<ActionResult<object>> FilterTaskList(Guid projectId, [FromBody] TaskFilterCriteria? criteria)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId);
        if (project is null)
            return NotFound();

        var c = criteria ?? TaskFilterCriteria.Default();
        HttpContext.Session.SetString(TaskFilterSession.KeyForProject(projectId), TaskFilterSession.Serialize(c));

        try
        {
            var result = await _taskService.GetFilteredTaskListAsync(
                projectId,
                workspaceId,
                userId,
                c,
                HttpContext.RequestAborted);

            return Ok(new { tasks = result.Tasks.Select(TaskCardJson).ToList() });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>UC-16: Đọc bộ lọc đã lưu trong Session (khởi tạo UI).</summary>
    [Authorize]
    [HttpGet("projects/{projectId:guid}/tasks/filter-state")]
    public async Task<ActionResult<TaskFilterCriteria>> GetFilterState(Guid projectId)
    {
        var workspaceId = User.GetWorkspaceId();
        var ok = await _db.Projects.AnyAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId);
        if (!ok)
            return NotFound();

        var json = HttpContext.Session.GetString(TaskFilterSession.KeyForProject(projectId));
        return Ok(TaskFilterSession.Deserialize(json));
    }

    /// <summary>UC-16: Xóa bộ lọc trong Session (mặc định).</summary>
    [Authorize]
    [HttpPost("projects/{projectId:guid}/tasks/filter-reset")]
    public async Task<ActionResult<TaskFilterCriteria>> ResetFilterState(Guid projectId)
    {
        var workspaceId = User.GetWorkspaceId();
        var ok = await _db.Projects.AnyAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId);
        if (!ok)
            return NotFound();

        HttpContext.Session.Remove(TaskFilterSession.KeyForProject(projectId));
        return Ok(TaskFilterCriteria.Default());
    }

    private static object ToKanbanFilterResponse(FilteredKanbanTasksResult r) => new
    {
        unassigned = r.Unassigned.Select(TaskCardJson).ToList(),
        pending = r.Pending.Select(TaskCardJson).ToList(),
        toDo = r.ToDo.Select(TaskCardJson).ToList(),
        inProgress = r.InProgress.Select(TaskCardJson).ToList(),
        review = r.Review.Select(TaskCardJson).ToList(),
        done = r.Done.Select(TaskCardJson).ToList()
    };

    private static object TaskCardJson(TaskCardVm vm) => new
    {
        taskId = vm.TaskId,
        title = vm.Title,
        projectId = vm.ProjectId,
        priority = vm.Priority.ToString(),
        dueDateUtc = vm.DueDateUtc,
        status = vm.Status.ToString(),
        assigneeUserId = vm.AssigneeUserId,
        assigneeDisplayName = vm.AssigneeDisplayName,
        assigneeAvatarUrl = vm.AssigneeAvatarUrl,
        isOverdue = vm.IsOverdue,
        canDrag = vm.CanDrag,
        createdAtUtc = vm.CreatedAtUtc
    };

    private async Task<object?> BuildTaskCardAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return null;

        var display = task.Status is TaskStatus.ToDo or TaskStatus.InProgress or TaskStatus.Review or TaskStatus.Done;
        if (!display)
            return null;

        var assignment = await _db.TaskAssignments.FirstOrDefaultAsync(a =>
            a.TaskId == taskId && a.Status == TaskAssignmentStatus.Accepted, cancellationToken);

        var assigneeUserId = assignment?.AssigneeUserId ?? string.Empty;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == assigneeUserId, cancellationToken);
        var assigneeDisplayName = user?.DisplayName ?? user?.Email ?? user?.UserName ?? "-";

        return new
        {
            taskId = task.Id,
            title = task.Title,
            projectId = task.ProjectId,
            priority = task.Priority.ToString(),
            dueDateUtc = task.DueDateUtc,
            status = task.Status.ToString(),
            assigneeUserId = assigneeUserId,
            assigneeDisplayName = assigneeDisplayName,
            assigneeAvatarUrl = user?.AvatarUrl
        };
    }

    [Authorize]
    [HttpPost("tasks/{taskId:guid}/evaluate")]
    public async Task<ActionResult<object>> Evaluate(Guid taskId, [FromBody] EvaluateTaskRequest request)
    {
        var userId = User.GetUserId();
        var workspaceId = User.GetWorkspaceId();

        MemberLevel? level = null;
        if (!string.IsNullOrWhiteSpace(request.NewLevel) && Enum.TryParse<MemberLevel>(request.NewLevel, true, out var parsed))
            level = parsed;

        var result = await _taskService.EvaluateTaskAsync(
            taskId,
            actorUserId: userId,
            workspaceId: workspaceId,
            score: request.Score,
            comment: request.Comment,
            newLevel: level,
            cancellationToken: HttpContext.RequestAborted);

        if (!result.Success)
            return BadRequest(result.ErrorMessage ?? "Không thể đánh giá task.");

        return Ok(new { taskId, score = request.Score });
    }

    private async Task<IReadOnlyList<object>> GetTaskSuggestionsAsync(
        Guid workspaceId,
        string? currentAssignee,
        CancellationToken cancellationToken)
    {
        // Suggest members with lower workload and higher level.
        var members = await _db.WorkspaceMembers
            .Where(m => m.WorkspaceId == workspaceId)
            .Select(m => new { m.UserId, m.Role, m.SubRole })
            .ToListAsync(cancellationToken);

        var userIds = members.Select(m => m.UserId).Distinct().ToList();

        var workloadByUser = await _db.TaskAssignments
            .Where(a => userIds.Contains(a.AssigneeUserId) && a.Status == TaskAssignmentStatus.Accepted)
            .Join(_db.Tasks, a => a.TaskId, t => t.Id, (a, t) => new { a.AssigneeUserId, t.Status, t.ProjectId })
            .Join(_db.Projects.Where(p => p.WorkspaceId == workspaceId), x => x.ProjectId, p => p.Id, (x, p) => x)
            .Where(x => x.Status == TaskStatus.InProgress)
            .GroupBy(x => x.AssigneeUserId)
            .Select(g => new { userId = g.Key, workload = g.Count() })
            .ToListAsync(cancellationToken);

        var workloadDict = workloadByUser.ToDictionary(x => x.userId, x => x.workload);

        var profiles = await _db.MemberProfiles
            .Where(p => userIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.Level })
            .ToListAsync(cancellationToken);

        var profileDict = profiles.ToDictionary(x => x.UserId, x => x.Level);

        var suggestions = members
            .Select(m => new
            {
                userId = m.UserId,
                level = profileDict.TryGetValue(m.UserId, out var lvl) ? lvl.ToString() : MemberLevel.Junior.ToString(),
                workload = workloadDict.TryGetValue(m.UserId, out var w) ? w : 0,
                role = m.Role.ToString(),
                subRole = m.SubRole
            })
            .OrderBy(x => x.workload)
            .ThenByDescending(x => x.level)
            .Take(3)
            .Cast<object>()
            .ToList();

        return suggestions;
    }

    /// <summary>UC-14: Làm mới danh sách gợi ý phân công (UC-04) khi MemberProfile thay đổi — dùng từ SignalR + fetch.</summary>
    [Authorize(Policy = "IsPM")]
    [HttpGet("tasks/suggested-assignees")]
    public async Task<ActionResult<IReadOnlyList<SuggestedAssigneeVm>>> GetSuggestedAssignees(
        [FromQuery] TaskPriority priority,
        CancellationToken cancellationToken)
    {
        var workspaceId = User.GetWorkspaceId();
        var list = await _taskService.GetSuggestedAssigneesAsync(workspaceId, priority, 5, cancellationToken);
        return Ok(list);
    }
}

