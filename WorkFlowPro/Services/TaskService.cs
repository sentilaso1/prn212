using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Services;

using TaskStatus = WorkFlowPro.Data.TaskStatus;

public sealed class TaskService : ITaskService
{
    private readonly WorkFlowProDbContext _db;
    private readonly IHubContext<TaskHub> _hub;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<TaskService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly INotificationService _notifications;

    // Student-scope upload limit.
    private const long MaxAttachmentBytes = 5L * 1024 * 1024; // 5MB

    public TaskService(
        WorkFlowProDbContext db,
        IHubContext<TaskHub> hub,
        IWebHostEnvironment env,
        ILogger<TaskService> logger,
        IHttpContextAccessor httpContextAccessor,
        INotificationService notifications)
    {
        _db = db;
        _hub = hub;
        _env = env;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _notifications = notifications;
    }

    public async Task<FilteredKanbanTasksResult> GetFilteredKanbanTasksAsync(
        Guid projectId,
        Guid workspaceId,
        string actorUserId,
        TaskFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("projectId is required.", nameof(projectId));
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("workspaceId is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ArgumentException("actorUserId is required.", nameof(actorUserId));

        criteria ??= TaskFilterCriteria.Default();

        var project = await _db.Projects.FirstOrDefaultAsync(
            p => p.Id == projectId && p.WorkspaceId == workspaceId,
            cancellationToken);
        if (project is null)
            throw new InvalidOperationException("Project không tồn tại trong workspace.");

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
                m.WorkspaceId == workspaceId &&
                m.UserId == actorUserId &&
                m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        var utcNow = DateTime.UtcNow;
        IQueryable<TaskItem> query = _db.Tasks.Where(t => t.ProjectId == projectId);
        query = ApplyMemberVisibilityFilter(_db, query, isPm, actorUserId);
        query = ApplyTaskFiltersQueryable(_db, query, criteria, utcNow, excludeCancelledWhenNoStatusFilter: true);

        var tasks = await query.AsNoTracking().ToListAsync(cancellationToken);
        var cards = await MapToTaskCardsAsync(tasks, actorUserId, isPm, utcNow, cancellationToken);

        var byStatus = cards.GroupBy(c => c.Status).ToDictionary(g => g.Key, g => g.ToList());

        TaskSortOption sort = criteria.Sort;
        List<TaskCardVm> Col(TaskStatus s) =>
            byStatus.TryGetValue(s, out var x) ? x : new List<TaskCardVm>();

        return new FilteredKanbanTasksResult
        {
            Unassigned = SortTaskCards(Col(TaskStatus.Unassigned), sort),
            Pending = SortTaskCards(Col(TaskStatus.Pending), sort),
            ToDo = SortTaskCards(Col(TaskStatus.ToDo), sort),
            InProgress = SortTaskCards(Col(TaskStatus.InProgress), sort),
            Review = SortTaskCards(Col(TaskStatus.Review), sort),
            Done = SortTaskCards(Col(TaskStatus.Done), sort)
        };
    }

    public async Task<FilteredTaskListResult> GetFilteredTaskListAsync(
        Guid projectId,
        Guid workspaceId,
        string actorUserId,
        TaskFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        if (projectId == Guid.Empty)
            throw new ArgumentException("projectId is required.", nameof(projectId));
        if (workspaceId == Guid.Empty)
            throw new ArgumentException("workspaceId is required.", nameof(workspaceId));
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ArgumentException("actorUserId is required.", nameof(actorUserId));

        criteria ??= TaskFilterCriteria.Default();

        var project = await _db.Projects.FirstOrDefaultAsync(
            p => p.Id == projectId && p.WorkspaceId == workspaceId,
            cancellationToken);
        if (project is null)
            throw new InvalidOperationException("Project không tồn tại trong workspace.");

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
                m.WorkspaceId == workspaceId &&
                m.UserId == actorUserId &&
                m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        var utcNow = DateTime.UtcNow;
        IQueryable<TaskItem> query = _db.Tasks.Where(t => t.ProjectId == projectId);
        query = ApplyMemberVisibilityFilter(_db, query, isPm, actorUserId);
        query = ApplyTaskFiltersQueryable(_db, query, criteria, utcNow, excludeCancelledWhenNoStatusFilter: true);

        var tasks = await query.AsNoTracking().ToListAsync(cancellationToken);
        var cards = await MapToTaskCardsAsync(tasks, actorUserId, isPm, utcNow, cancellationToken);
        var sorted = SortTaskCards(cards, criteria.Sort);

        return new FilteredTaskListResult { Tasks = sorted };
    }

    public async Task<IReadOnlyList<WorkspaceMemberFilterOptionVm>> GetWorkspaceMemberFilterOptionsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty)
            return Array.Empty<WorkspaceMemberFilterOptionVm>();

        var rows = await (
            from wm in _db.WorkspaceMembers
            where wm.WorkspaceId == workspaceId
            join u in _db.Users on wm.UserId equals u.Id
            join p in _db.MemberProfiles on wm.UserId equals p.UserId into profs
            from prof in profs.DefaultIfEmpty()
            orderby u.DisplayName ?? u.Email ?? u.UserName
            select new WorkspaceMemberFilterOptionVm(
                wm.UserId,
                u.DisplayName ?? u.Email ?? u.UserName ?? wm.UserId,
                prof != null ? prof.Level : (MemberLevel?)null))
            .ToListAsync(cancellationToken);

        return rows;
    }

    /// <summary>UC-16: Base query — PM xem tất cả; Member chỉ task được giao (Pending/Accepted).</summary>
    public static IQueryable<TaskItem> ApplyMemberVisibilityFilter(
        WorkFlowProDbContext db,
        IQueryable<TaskItem> tasks,
        bool isPm,
        string userId)
    {
        if (isPm)
            return tasks;

        return tasks.Where(t =>
            db.TaskAssignments.Any(a =>
                a.TaskId == t.Id &&
                a.AssigneeUserId == userId &&
                (a.Status == TaskAssignmentStatus.Accepted || a.Status == TaskAssignmentStatus.Pending)));
    }

    /// <summary>UC-16: Áp dụng Where cho IQueryable (tối ưu SQL một lần).</summary>
    public static IQueryable<TaskItem> ApplyTaskFiltersQueryable(
        WorkFlowProDbContext db,
        IQueryable<TaskItem> query,
        TaskFilterCriteria criteria,
        DateTime utcNow,
        bool excludeCancelledWhenNoStatusFilter)
    {
        if (criteria.Statuses is null)
        {
            if (excludeCancelledWhenNoStatusFilter)
                query = query.Where(t => t.Status != TaskStatus.Cancelled);
        }
        else if (criteria.Statuses.Count == 0)
        {
            query = query.Where(_ => false);
        }
        else
        {
            var set = criteria.Statuses.Distinct().ToList();
            query = query.Where(t => set.Contains(t.Status));
        }

        if (criteria.Priorities is null)
        {
            // no priority filter
        }
        else if (criteria.Priorities.Count == 0)
        {
            query = query.Where(_ => false);
        }
        else
        {
            var pri = criteria.Priorities.Distinct().ToList();
            query = query.Where(t => pri.Contains(t.Priority));
        }

        var search = criteria.SearchTitle?.Trim();
        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => t.Title.Contains(search));

        if (criteria.AssigneeUserIds is { Count: > 0 })
        {
            var ids = criteria.AssigneeUserIds
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct()
                .ToList();

            if (ids.Count > 0)
            {
                query = query.Where(t =>
                    db.TaskAssignments.Any(a =>
                        a.TaskId == t.Id &&
                        a.Status == TaskAssignmentStatus.Accepted &&
                        ids.Contains(a.AssigneeUserId)));
            }
        }

        query = criteria.DueDateBucket switch
        {
            TaskDueDateBucket.Today => query.Where(t =>
                t.DueDateUtc != null &&
                t.DueDateUtc.Value.Date == utcNow.Date),
            TaskDueDateBucket.ThisWeek => query.Where(t =>
                t.DueDateUtc != null &&
                t.DueDateUtc.Value >= StartOfWeekUtc(utcNow) &&
                t.DueDateUtc.Value < StartOfWeekUtc(utcNow).AddDays(7)),
            TaskDueDateBucket.ThisMonth => query.Where(t =>
                t.DueDateUtc != null &&
                t.DueDateUtc.Value.Year == utcNow.Year &&
                t.DueDateUtc.Value.Month == utcNow.Month),
            TaskDueDateBucket.Overdue => query.Where(t =>
                t.DueDateUtc != null &&
                t.DueDateUtc < utcNow &&
                t.Status != TaskStatus.Done),
            _ => query
        };

        return query;
    }

    private static DateTime StartOfWeekUtc(DateTime utcNow)
    {
        var d = utcNow.Date;
        int day = (int)d.DayOfWeek;
        int diff = day == (int)DayOfWeek.Sunday ? -6 : 1 - day;
        return d.AddDays(diff);
    }

    private static List<TaskCardVm> SortTaskCards(IReadOnlyList<TaskCardVm> cards, TaskSortOption sort)
    {
        IEnumerable<TaskCardVm> ordered = sort switch
        {
            TaskSortOption.DueDateAsc => cards
                .OrderBy(c => c.DueDateUtc.HasValue ? 0 : 1)
                .ThenBy(c => c.DueDateUtc)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            TaskSortOption.DueDateDesc => cards
                .OrderBy(c => c.DueDateUtc.HasValue ? 0 : 1)
                .ThenByDescending(c => c.DueDateUtc)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            TaskSortOption.PriorityAsc => cards
                .OrderBy(c => c.Priority)
                .ThenBy(c => c.DueDateUtc)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            TaskSortOption.PriorityDesc => cards
                .OrderByDescending(c => c.Priority)
                .ThenBy(c => c.DueDateUtc)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            TaskSortOption.CreatedAtAsc => cards
                .OrderBy(c => c.CreatedAtUtc)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            TaskSortOption.CreatedAtDesc => cards
                .OrderByDescending(c => c.CreatedAtUtc)
                .ThenBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            TaskSortOption.TitleAsc => cards.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            TaskSortOption.TitleDesc => cards.OrderByDescending(c => c.Title, StringComparer.OrdinalIgnoreCase),
            _ => cards.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
        };

        return ordered.ToList();
    }

    private async Task<List<TaskCardVm>> MapToTaskCardsAsync(
        IReadOnlyList<TaskItem> tasks,
        string actorUserId,
        bool isPm,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (tasks.Count == 0)
            return new List<TaskCardVm>();

        var taskIds = tasks.Select(t => t.Id).ToList();
        var assignments = await _db.TaskAssignments
            .AsNoTracking()
            .Where(a => taskIds.Contains(a.TaskId) &&
                        (a.Status == TaskAssignmentStatus.Accepted || a.Status == TaskAssignmentStatus.Pending))
            .ToListAsync(cancellationToken);

        var assignmentByTaskId = assignments
            .GroupBy(a => a.TaskId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(a => a.Status == TaskAssignmentStatus.Accepted).First());

        var assigneeUserIds = assignments.Select(a => a.AssigneeUserId).Distinct().ToList();
        var users = await _db.Users
            .AsNoTracking()
            .Where(u => assigneeUserIds.Contains(u.Id))
            .ToListAsync(cancellationToken);

        var userById = users.ToDictionary(u => u.Id, u => u);

        var draggable = new HashSet<TaskStatus>
        {
            TaskStatus.ToDo,
            TaskStatus.InProgress,
            TaskStatus.Review,
            TaskStatus.Done
        };
        if (isPm)
        {
            draggable.Add(TaskStatus.Unassigned);
            draggable.Add(TaskStatus.Pending);
        }

        var list = new List<TaskCardVm>(tasks.Count);
        foreach (var t in tasks)
        {
            assignmentByTaskId.TryGetValue(t.Id, out var assignment);
            var assigneeUserId = assignment?.AssigneeUserId;
            userById.TryGetValue(assigneeUserId ?? string.Empty, out var assigneeUser);

            var canDrag = draggable.Contains(t.Status);

            // Member can only drag their own accepted tasks.
            if (!isPm && t.Status == TaskStatus.ToDo)
            {
                var isAcceptedByMe = assignments.Any(a =>
                    a.TaskId == t.Id &&
                    a.AssigneeUserId == actorUserId &&
                    a.Status == TaskAssignmentStatus.Accepted);
                if (!isAcceptedByMe) canDrag = false;
            }

            var isOverdue = t.DueDateUtc.HasValue
                            && t.DueDateUtc.Value < utcNow
                            && t.Status != TaskStatus.Done;

            var displayName = assigneeUser?.DisplayName ?? assigneeUser?.Email ?? assigneeUser?.UserName ?? "-";
            if (assignment is not null && assignment.Status == TaskAssignmentStatus.Pending && assigneeUser is not null)
                displayName += " (chờ xác nhận)";

            list.Add(new TaskCardVm(
                taskId: t.Id,
                title: t.Title,
                projectId: t.ProjectId,
                priority: t.Priority,
                dueDateUtc: t.DueDateUtc,
                status: t.Status,
                assigneeUserId: assigneeUserId ?? string.Empty,
                assigneeDisplayName: displayName,
                assigneeAvatarUrl: assigneeUser?.AvatarUrl,
                isOverdue: isOverdue,
                canDrag: canDrag,
                createdAtUtc: t.CreatedAtUtc));
        }

        return list;
    }

    public async Task<IReadOnlyList<SuggestedAssigneeVm>> GetSuggestedAssigneesAsync(
        Guid workspaceId,
        TaskPriority priority,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (workspaceId == Guid.Empty)
            return Array.Empty<SuggestedAssigneeVm>();

        if (take <= 0)
            take = 5;

        var requiredLevel = priority switch
        {
            TaskPriority.Low => MemberLevel.Junior,
            TaskPriority.Medium => MemberLevel.Mid,
            TaskPriority.High => MemberLevel.Mid,
            TaskPriority.Critical => MemberLevel.Senior,
            _ => MemberLevel.Junior
        };

        // Only non-PM members.
        var candidates = await (
            from wm in _db.WorkspaceMembers
            where wm.WorkspaceId == workspaceId && wm.Role != WorkspaceMemberRole.PM
            join user in _db.Users on wm.UserId equals user.Id
            join p in _db.MemberProfiles on wm.UserId equals p.UserId into profs
            from prof in profs.DefaultIfEmpty()
            select new SuggestedAssigneeVm(
                wm.UserId,
                user.DisplayName ?? user.Email ?? user.UserName ?? wm.UserId,
                prof != null ? prof.Level : MemberLevel.Junior,
                prof != null ? prof.CompletionRate : 0m,
                prof != null ? prof.AvgScore : 0m,
                prof != null ? prof.CurrentWorkload : 0)
        ).ToListAsync(cancellationToken);

        if (candidates.Count == 0)
            return Array.Empty<SuggestedAssigneeVm>();

        // If any candidate matches required level, prefer them. Otherwise, fallback to all.
        var matching = candidates.Where(c => c.Level >= requiredLevel).ToList();
        var workingSet = matching.Count > 0 ? matching : candidates;

        // Smart scoring (student-scope heuristic):
        // 1) Level phù hợp (>= requiredLevel)
        // 2) CurrentWorkload thấp
        // 3) CompletionRate cao
        // 4) AvgScore cao
        var ordered = workingSet
            .OrderByDescending(c => c.Level >= requiredLevel)
            .ThenBy(c => c.CurrentWorkload)
            .ThenByDescending(c => c.CompletionRate)
            .ThenByDescending(c => c.AvgScore)
            .Take(Math.Min(5, take))
            .ToList();

        return ordered;
    }

    public async Task<TaskDetailVm> GetTaskDetailAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        if (taskId == Guid.Empty)
            throw new ArgumentException("taskId is required.", nameof(taskId));

        if (workspaceId == Guid.Empty)
            throw new ArgumentException("workspaceId is required.", nameof(workspaceId));

        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new ArgumentException("actorUserId is required.", nameof(actorUserId));

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            throw new InvalidOperationException("Task không tồn tại.");

        // Global query filters should already isolate by workspace, but we still validate for safety.
        if (task.ProjectId == Guid.Empty)
            throw new InvalidOperationException("Task project không hợp lệ.");

        var project = await _db.Projects.FirstOrDefaultAsync(
            p => p.Id == task.ProjectId && p.WorkspaceId == workspaceId,
            cancellationToken);

        if (project is null)
            throw new InvalidOperationException("Project không hợp lệ.");

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId && m.UserId == actorUserId && m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        var assignmentForActor = await _db.TaskAssignments
            .Where(a => a.TaskId == taskId &&
                        a.AssigneeUserId == actorUserId &&
                        (a.Status == TaskAssignmentStatus.Accepted || a.Status == TaskAssignmentStatus.Pending))
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var isAssignee = assignmentForActor is not null;

        // Determine "current assignee" for display.
        var acceptedAssignment = await _db.TaskAssignments
            .Where(a => a.TaskId == taskId && a.Status == TaskAssignmentStatus.Accepted)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var pendingAssignment = acceptedAssignment is null
            ? await _db.TaskAssignments
                .Where(a => a.TaskId == taskId && a.Status == TaskAssignmentStatus.Pending)
                .OrderByDescending(a => a.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var currentAssignment = acceptedAssignment ?? pendingAssignment;

        string? assigneeUserId = currentAssignment?.AssigneeUserId;
        string assigneeDisplayName = "-";
        string? assigneeAvatarUrl = null;
        if (!string.IsNullOrWhiteSpace(assigneeUserId))
        {
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == assigneeUserId, cancellationToken);
            assigneeDisplayName = user?.DisplayName ?? user?.Email ?? user?.UserName ?? "-";
            assigneeAvatarUrl = user?.AvatarUrl;
        }

        // Authorization: PM can see all, Member only their assigned tasks.
        if (!isPm && !isAssignee)
            throw new UnauthorizedAccessException("Bạn không có quyền xem task này.");

        var createdBy = await _db.Users.FirstOrDefaultAsync(u => u.Id == task.CreatedByUserId, cancellationToken);

        var showAcceptReject = task.Status == TaskStatus.Pending &&
                                 assignmentForActor?.Status == TaskAssignmentStatus.Pending &&
                                 assignmentForActor.AssigneeUserId == actorUserId;

        return new TaskDetailVm(
            TaskId: task.Id,
            ProjectId: project.Id,
            ProjectName: project.Name,
            Title: task.Title,
            Description: task.Description,
            Priority: task.Priority,
            DueDateUtc: task.DueDateUtc,
            Status: task.Status,
            AssigneeUserId: assigneeUserId,
            AssigneeDisplayName: assigneeDisplayName,
            AssigneeAvatarUrl: assigneeAvatarUrl,
            CreatedByUserId: task.CreatedByUserId,
            CreatedByDisplayName: createdBy?.DisplayName ?? createdBy?.Email ?? createdBy?.UserName ?? task.CreatedByUserId,
            CreatedAtUtc: task.CreatedAtUtc,
            UpdatedAtUtc: task.UpdatedAtUtc,
            IsPm: isPm,
            IsAssignee: isAssignee,
            CanEditCore: isPm,
            CanEditDescription: isPm || isAssignee,
            CanUploadAttachments: isPm || isAssignee,
            CanManageComments: isPm || isAssignee,
            ShowAcceptReject: showAcceptReject);
    }

    public async Task<IReadOnlyList<AttachmentVm>> GetTaskAttachmentsAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        // If we can load detail, we already passed authorization.

        var attachments = await (
            from att in _db.Attachments
            where att.TaskId == taskId
            join u in _db.Users on att.UploadedByUserId equals u.Id into users
            from uploader in users.DefaultIfEmpty()
            orderby att.UploadedAtUtc descending
            select new AttachmentVm(
                att.Id,
                att.TaskId,
                att.FileName,
                att.FileUrl,
                att.FileSizeBytes,
                att.UploadedByUserId,
                uploader != null ? (uploader.DisplayName ?? uploader.Email ?? uploader.UserName ?? uploader.Id) : att.UploadedByUserId,
                att.UploadedAtUtc))
            .ToListAsync(cancellationToken);

        return attachments;
    }

    public async Task<IReadOnlyList<CommentNodeVm>> GetTaskCommentsAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken); // authorization

        var comments = await (
            from c in _db.TaskComments
            where c.TaskId == taskId
            orderby c.CreatedAtUtc ascending
            join u in _db.Users on c.UserId equals u.Id
            select new
            {
                c.Id,
                c.TaskId,
                c.UserId,
                UserDisplayName = u.DisplayName ?? u.Email ?? u.UserName ?? u.Id,
                c.ParentCommentId,
                c.Content,
                c.IsDeleted,
                c.CreatedAtUtc,
                c.UpdatedAtUtc
            })
            .ToListAsync(cancellationToken);

        // Build tree.
        var builders = comments.ToDictionary(
            x => x.Id,
            x => new CommentBuilder(
                x.Id,
                x.TaskId,
                x.UserId,
                x.UserDisplayName,
                x.ParentCommentId,
                x.Content,
                x.IsDeleted,
                x.CreatedAtUtc,
                x.UpdatedAtUtc));

        var roots = new List<CommentBuilder>();
        foreach (var b in builders.Values)
        {
            if (b.ParentCommentId is null)
            {
                roots.Add(b);
                continue;
            }

            if (builders.TryGetValue(b.ParentCommentId.Value, out var parent))
                parent.Replies.Add(b);
            else
                roots.Add(b);
        }

        static CommentNodeVm ToVm(CommentBuilder b)
        {
            return new CommentNodeVm(
                b.Id,
                b.TaskId,
                b.UserId,
                b.UserDisplayName,
                b.ParentCommentId,
                b.Content,
                b.IsDeleted,
                b.CreatedAtUtc,
                b.UpdatedAtUtc,
                b.Replies
                    .OrderBy(r => r.CreatedAtUtc)
                    .Select(ToVm)
                    .ToList());
        }

        return roots
            .OrderBy(r => r.CreatedAtUtc)
            .Select(ToVm)
            .ToList();
    }

    private sealed class CommentBuilder
    {
        public CommentBuilder(
            Guid id,
            Guid taskId,
            string userId,
            string userDisplayName,
            Guid? parentCommentId,
            string content,
            bool isDeleted,
            DateTime createdAtUtc,
            DateTime? updatedAtUtc)
        {
            Id = id;
            TaskId = taskId;
            UserId = userId;
            UserDisplayName = userDisplayName;
            ParentCommentId = parentCommentId;
            Content = content;
            IsDeleted = isDeleted;
            CreatedAtUtc = createdAtUtc;
            UpdatedAtUtc = updatedAtUtc;
            Replies = new List<CommentBuilder>();
        }

        public Guid Id { get; }
        public Guid TaskId { get; }
        public string UserId { get; }
        public string UserDisplayName { get; }
        public Guid? ParentCommentId { get; }
        public string Content { get; }
        public bool IsDeleted { get; }
        public DateTime CreatedAtUtc { get; }
        public DateTime? UpdatedAtUtc { get; }
        public List<CommentBuilder> Replies { get; }
    }

    public async Task<IReadOnlyList<HistoryEntryVm>> GetTaskHistoryAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken); // authorization

        var entries = await (
            from h in _db.TaskHistoryEntries
            where h.TaskId == taskId
            join u in _db.Users on h.ActorUserId equals u.Id into users
            from actor in users.DefaultIfEmpty()
            orderby h.TimestampUtc descending
            select new HistoryEntryVm(
                h.Id,
                h.TaskId,
                h.ActorUserId,
                actor != null ? (actor.DisplayName ?? actor.Email ?? actor.UserName ?? actor.Id) : h.ActorUserId,
                actor != null ? actor.AvatarUrl : null,
                h.Action,
                h.OldValue,
                h.NewValue,
                h.TimestampUtc))
            .ToListAsync(cancellationToken);

        return entries;
    }

    // UC-18 convenience overload (for callers that can rely on current Claims).
    public async Task<IReadOnlyList<HistoryEntryVm>> GetTaskHistoryAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var http = _httpContextAccessor.HttpContext;
        var principal = http?.User;
        if (principal is null)
            throw new UnauthorizedAccessException();

        var actorUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            throw new UnauthorizedAccessException();

        var workspaceIdStr = principal.FindFirstValue("CurrentWorkspaceId") ?? principal.FindFirstValue("workspace_id");
        if (!Guid.TryParse(workspaceIdStr, out var workspaceId))
            throw new UnauthorizedAccessException();

        return await GetTaskHistoryAsync(taskId, actorUserId, workspaceId, cancellationToken);
    }

    public async Task<TaskEvaluationVm?> GetTaskEvaluationAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        if (!detail.IsPm)
            return null;

        var eval = await (
            from e in _db.TaskEvaluations
            where e.TaskId == taskId
            join u in _db.Users on e.PmUserId equals u.Id into users
            from pm in users.DefaultIfEmpty()
            orderby e.EvaluatedAtUtc descending
            select new TaskEvaluationVm(
                e.Id,
                e.TaskId,
                e.PmUserId,
                pm != null ? (pm.DisplayName ?? pm.Email ?? pm.UserName ?? pm.Id) : e.PmUserId,
                pm != null ? pm.AvatarUrl : null,
                e.Score,
                e.Comment,
                e.EvaluatedAtUtc,
                e.IsLocked))
            .FirstOrDefaultAsync(cancellationToken);

        return eval;
    }

    public async Task<IReadOnlyList<TaskEvaluationVm>> GetTaskEvaluationsAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        if (!detail.IsPm)
            return Array.Empty<TaskEvaluationVm>();

        var list = await (
            from e in _db.TaskEvaluations
            where e.TaskId == taskId
            join u in _db.Users on e.PmUserId equals u.Id into users
            from pm in users.DefaultIfEmpty()
            orderby e.EvaluatedAtUtc descending
            select new TaskEvaluationVm(
                e.Id,
                e.TaskId,
                e.PmUserId,
                pm != null ? (pm.DisplayName ?? pm.Email ?? pm.UserName ?? pm.Id) : e.PmUserId,
                pm != null ? pm.AvatarUrl : null,
                e.Score,
                e.Comment,
                e.EvaluatedAtUtc,
                e.IsLocked))
            .ToListAsync(cancellationToken);

        return list;
    }

    public async Task<TaskUpdateResult> UpdateTaskAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        TaskUpdateRequest request,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return new TaskUpdateResult(false, "Task không tồn tại.");

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var now = DateTime.UtcNow;
        string? oldTitle = task.Title;
        TaskPriority oldPriority = task.Priority;
        TaskStatus oldStatus = task.Status;
        DateTime? oldDue = task.DueDateUtc;
        string? oldDesc = task.Description;

        bool assigneeChanged = false;
        string? normalizedAssignee = string.IsNullOrWhiteSpace(request.NewAssigneeUserId)
            ? null
            : request.NewAssigneeUserId.Trim();

        if (!detail.CanEditCore)
        {
            // Member is allowed to edit only description.
            if (request.Title is not null || request.Priority is not null || request.DueDateUtc is not null)
                return new TaskUpdateResult(false, "Bạn không có quyền chỉnh sửa thông tin cốt lõi của task.");

            if (request.NewAssigneeUserId is not null)
                return new TaskUpdateResult(false, "Bạn không có quyền thay đổi assignee.");
        }

        // Title / priority / due-date only for PM.
        if (detail.CanEditCore)
        {
            if (!string.IsNullOrWhiteSpace(request.Title))
                task.Title = request.Title.Trim();

            if (request.Priority is not null)
                task.Priority = request.Priority.Value;

            if (request.DueDateUtc.HasValue)
                task.DueDateUtc = request.DueDateUtc.Value;
        }

        // Description allowed for PM and assignee.
        if (detail.CanEditDescription && request.Description is not null)
            task.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();

        // Assignee update only when PM and task is in Unassigned/Pending (state machine safe).
        if (detail.CanEditCore && request.NewAssigneeUserId is not null)
        {
            if (task.Status is not TaskStatus.Unassigned and not TaskStatus.Pending)
                return new TaskUpdateResult(false, "Chỉ có thể thay đổi assignee khi task ở trạng thái Unassigned hoặc Pending.");

            assigneeChanged = true;

            // Remove existing Pending assignments (they haven't been accepted yet).
            var existingPending = await _db.TaskAssignments
                .Where(a => a.TaskId == taskId && a.Status == TaskAssignmentStatus.Pending)
                .ToListAsync(cancellationToken);
            if (existingPending.Count > 0)
                _db.TaskAssignments.RemoveRange(existingPending);

            if (normalizedAssignee is null)
            {
                task.Status = TaskStatus.Unassigned;
            }
            else
            {
                var assigneeIsWorkspaceMember = await _db.WorkspaceMembers.AnyAsync(m =>
                    m.WorkspaceId == workspaceId &&
                    m.UserId == normalizedAssignee &&
                    m.Role != WorkspaceMemberRole.PM,
                    cancellationToken);
                if (!assigneeIsWorkspaceMember)
                    return new TaskUpdateResult(false, "Assignee phải là Member trong workspace.");

                _db.TaskAssignments.Add(new TaskAssignment
                {
                    TaskId = taskId,
                    AssigneeUserId = normalizedAssignee,
                    Status = TaskAssignmentStatus.Pending
                });

                task.Status = TaskStatus.Pending;
            }
        }

        task.UpdatedAtUtc = now;

        // UC-18: granular history entries for overview edits.
        static string? Trunc(string? value)
        {
            if (value is null)
                return null;
            if (value.Length <= 500)
                return value;
            return value[..500];
        }

        static string FormatDate(DateTime? dtUtc) =>
            dtUtc.HasValue ? dtUtc.Value.ToString("yyyy-MM-dd") : "-";

        var entries = new List<TaskHistoryEntry>(capacity: 6);

        // Title (PM only).
        if (detail.CanEditCore && !string.Equals(oldTitle, task.Title, StringComparison.Ordinal))
        {
            entries.Add(new TaskHistoryEntry
            {
                TaskId = taskId,
                ActorUserId = actorUserId,
                Action = "UpdatedTitle",
                OldValue = Trunc(oldTitle),
                NewValue = Trunc(task.Title),
                TimestampUtc = now
            });
        }

        // Description (PM/assignee).
        if (detail.CanEditDescription && !string.Equals(oldDesc ?? "", task.Description ?? "", StringComparison.Ordinal))
        {
            entries.Add(new TaskHistoryEntry
            {
                TaskId = taskId,
                ActorUserId = actorUserId,
                Action = "UpdatedDescription",
                OldValue = Trunc(oldDesc),
                NewValue = Trunc(task.Description),
                TimestampUtc = now
            });
        }

        // Priority (PM only).
        if (detail.CanEditCore && oldPriority != task.Priority)
        {
            entries.Add(new TaskHistoryEntry
            {
                TaskId = taskId,
                ActorUserId = actorUserId,
                Action = "UpdatedPriority",
                OldValue = oldPriority.ToString(),
                NewValue = task.Priority.ToString(),
                TimestampUtc = now
            });
        }

        // Due date (PM only).
        if (detail.CanEditCore && oldDue != task.DueDateUtc)
        {
            entries.Add(new TaskHistoryEntry
            {
                TaskId = taskId,
                ActorUserId = actorUserId,
                Action = "UpdatedDueDateUtc",
                OldValue = FormatDate(oldDue),
                NewValue = FormatDate(task.DueDateUtc),
                TimestampUtc = now
            });
        }

        // Assignee + derived status change (PM only).
        if (assigneeChanged)
        {
            var oldAssignee = detail.AssigneeUserId ?? "(none)";
            var newAssignee = normalizedAssignee ?? "(none)";

            entries.Add(new TaskHistoryEntry
            {
                TaskId = taskId,
                ActorUserId = actorUserId,
                Action = "UpdatedAssignee",
                OldValue = oldAssignee,
                NewValue = newAssignee,
                TimestampUtc = now
            });
        }

        if (oldStatus != task.Status)
        {
            entries.Add(new TaskHistoryEntry
            {
                TaskId = taskId,
                ActorUserId = actorUserId,
                Action = "UpdatedStatus",
                OldValue = oldStatus.ToString(),
                NewValue = task.Status.ToString(),
                TimestampUtc = now
            });
        }

        foreach (var e in entries)
            _db.TaskHistoryEntries.Add(e);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // UC-07 realtime: refresh overview and activity log.
        var projectId = task.ProjectId;
        await _hub.Clients.Group(projectId.ToString("D")).SendAsync(
            "TaskUpdated",
            new { taskId = taskId, newStatus = task.Status.ToString() },
            cancellationToken);

        if (assigneeChanged && normalizedAssignee is not null)
        {
            await _notifications.CreateAndPushAsync(
                userId: normalizedAssignee,
                type: NotificationType.TaskAssignedPending,
                message: $"Bạn được giao task \"{task.Title}\".",
                workspaceId: workspaceId,
                projectId: projectId,
                taskId: taskId,
                redirectUrl: $"/Tasks/AcceptReject/{taskId}?workspaceId={workspaceId:D}",
                cancellationToken: cancellationToken);
        }

        return new TaskUpdateResult(true, null);
    }

    public async Task<AttachmentUploadResult> AddAttachmentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        if (!detail.CanUploadAttachments)
            return new AttachmentUploadResult(false, "Bạn không có quyền upload attachment.", null);

        if (file is null || file.Length == 0)
            return new AttachmentUploadResult(false, "File không hợp lệ.", null);

        if (file.Length > MaxAttachmentBytes)
            return new AttachmentUploadResult(false, $"File tối đa {MaxAttachmentBytes / (1024 * 1024)}MB.", null);

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return new AttachmentUploadResult(false, "Task không tồn tại.", null);

        var originalFileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(originalFileName))
            originalFileName = "attachment";

        if (originalFileName.Length > 255)
            originalFileName = originalFileName.Substring(originalFileName.Length - 255);

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".bin";

        var safeFolder = Path.Combine("uploads", "tasks", taskId.ToString("D"));
        var absFolder = Path.Combine(_env.WebRootPath, safeFolder);
        Directory.CreateDirectory(absFolder);

        var storedFileName = $"{Guid.NewGuid():N}{ext}";
        var absPath = Path.Combine(absFolder, storedFileName);

        await using (var stream = System.IO.File.Create(absPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        var fileUrl = $"/{safeFolder.Replace('\\', '/')}/{storedFileName}";
        var now = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var attachment = new Attachment
        {
            TaskId = taskId,
            FileName = originalFileName,
            FileUrl = fileUrl,
            FileSizeBytes = file.Length,
            UploadedByUserId = actorUserId,
            UploadedAtUtc = now
        };

        _db.Attachments.Add(attachment);

        // History entry + broadcast.
        var entry = new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actorUserId,
            Action = "Thêm attachment",
            OldValue = null,
            NewValue = originalFileName.Length > 500 ? originalFileName[..500] : originalFileName,
            TimestampUtc = now
        };
        _db.TaskHistoryEntries.Add(entry);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var uploaderDisplay = await GetUserDisplayNameAsync(actorUserId, cancellationToken);

        await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
            "AttachmentAdded",
            new { taskId = taskId, attachmentId = attachment.Id },
            cancellationToken);

        await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
            "HistoryAdded",
            new { taskId = taskId, entryId = entry.Id },
            cancellationToken);

        return new AttachmentUploadResult(true, null, new AttachmentVm(
            attachment.Id,
            attachment.TaskId,
            attachment.FileName,
            attachment.FileUrl,
            attachment.FileSizeBytes,
            attachment.UploadedByUserId,
            uploaderDisplay,
            attachment.UploadedAtUtc));
    }

    public async Task<TaskUpdateResult> DeleteAttachmentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        if (!detail.CanUploadAttachments)
            return new TaskUpdateResult(false, "Bạn không có quyền xoá attachment.");

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return new TaskUpdateResult(false, "Task không tồn tại.");

        var attachment = await _db.Attachments.FirstOrDefaultAsync(a =>
            a.Id == attachmentId && a.TaskId == taskId, cancellationToken);
        if (attachment is null)
            return new TaskUpdateResult(false, "Attachment không tồn tại.");

        var now = DateTime.UtcNow;
        var fileUrl = attachment.FileUrl;
        var fileName = attachment.FileName;

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Attachments.Remove(attachment);

        var entry = new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actorUserId,
            Action = "Xoá attachment",
            OldValue = fileName.Length > 500 ? fileName[..500] : fileName,
            NewValue = null,
            TimestampUtc = now
        };
        _db.TaskHistoryEntries.Add(entry);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        // Best-effort delete physical file (if inside wwwroot).
        try
        {
            if (!string.IsNullOrWhiteSpace(fileUrl) && fileUrl.StartsWith("/", StringComparison.Ordinal))
            {
                var rel = fileUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var abs = Path.Combine(_env.WebRootPath, rel);
                if (System.IO.File.Exists(abs))
                    System.IO.File.Delete(abs);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete attachment file for {AttachmentId}", attachmentId);
        }

        await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
            "AttachmentDeleted",
            new { taskId = taskId, attachmentId = attachmentId },
            cancellationToken);

        await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
            "HistoryAdded",
            new { taskId = taskId, entryId = entry.Id },
            cancellationToken);

        return new TaskUpdateResult(true, null);
    }

    public async Task<CommentActionResult> AddCommentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        string content,
        Guid? parentCommentId,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        if (!detail.CanManageComments)
            return new CommentActionResult(false, "Bạn không có quyền bình luận.");

        content = content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            return new CommentActionResult(false, "Nội dung comment không được trống.");

        if (content.Length > 4000)
            return new CommentActionResult(false, "Nội dung comment tối đa 4000 ký tự.");

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return new CommentActionResult(false, "Task không tồn tại.");

        if (parentCommentId is not null)
        {
            var parentExists = await _db.TaskComments.AnyAsync(c =>
                c.Id == parentCommentId.Value && c.TaskId == taskId, cancellationToken);
            if (!parentExists)
                return new CommentActionResult(false, "ParentCommentId không hợp lệ.");
        }

        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var comment = new TaskComment
        {
            TaskId = taskId,
            UserId = actorUserId,
            ParentCommentId = parentCommentId,
            Content = content,
            IsDeleted = false,
            CreatedAtUtc = now
        };
        _db.TaskComments.Add(comment);

        var entry = new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actorUserId,
            Action = "Thêm comment",
            OldValue = null,
            NewValue = content.Length > 500 ? content[..500] : content,
            TimestampUtc = now
        };
        _db.TaskHistoryEntries.Add(entry);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var actorDisplayName = await GetUserDisplayNameAsync(actorUserId, cancellationToken);

        // UC-11: thông báo comment (PM + assignee + creator, trừ người gửi).
        try
        {
            var project = await _db.Projects.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);
            if (project is not null)
            {
                var wsId = project.WorkspaceId;
                var recipientIds = new HashSet<string>(StringComparer.Ordinal);
                var pmIds = await _db.WorkspaceMembers.AsNoTracking()
                    .Where(m => m.WorkspaceId == wsId && m.Role == WorkspaceMemberRole.PM)
                    .Select(m => m.UserId)
                    .ToListAsync(cancellationToken);
                foreach (var u in pmIds)
                {
                    if (u != actorUserId)
                        recipientIds.Add(u);
                }

                var assigneeId = await _db.TaskAssignments.AsNoTracking()
                    .Where(a => a.TaskId == taskId && a.Status == TaskAssignmentStatus.Accepted)
                    .Select(a => a.AssigneeUserId)
                    .FirstOrDefaultAsync(cancellationToken);
                if (!string.IsNullOrEmpty(assigneeId) && assigneeId != actorUserId)
                    recipientIds.Add(assigneeId);
                if (task.CreatedByUserId != actorUserId)
                    recipientIds.Add(task.CreatedByUserId);

                var msg = $"{actorDisplayName} vừa bình luận trên task \"{task.Title}\".";
                var redirect = $"/Tasks/Details/{taskId}";
                foreach (var uid in recipientIds)
                {
                    await _notifications.CreateAndPushAsync(
                        uid,
                        NotificationType.TaskCommentAdded,
                        msg,
                        workspaceId: wsId,
                        projectId: task.ProjectId,
                        taskId: taskId,
                        redirectUrl: redirect,
                        cancellationToken: cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UC-11: failed to push comment notifications for task {TaskId}", taskId);
        }

        await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
            "CommentAdded",
            new
            {
                taskId = taskId,
                comment = new
                {
                    id = comment.Id,
                    taskId = comment.TaskId,
                    userId = comment.UserId,
                    userDisplayName = actorDisplayName,
                    parentCommentId = comment.ParentCommentId,
                    content = comment.Content,
                    isDeleted = comment.IsDeleted,
                    createdAtUtc = comment.CreatedAtUtc,
                    updatedAtUtc = comment.UpdatedAtUtc
                },
                actor = new { userId = actorUserId, displayName = actorDisplayName }
            },
            cancellationToken);

        await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
            "HistoryAdded",
            new { taskId = taskId, entryId = entry.Id },
            cancellationToken);

        return new CommentActionResult(true, null);
    }

    public async Task<CommentActionResult> EditCommentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        Guid commentId,
        string content,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        if (!detail.CanManageComments)
            return new CommentActionResult(false, "Bạn không có quyền bình luận.");

        content = content?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(content))
            return new CommentActionResult(false, "Nội dung comment không được trống.");

        if (content.Length > 4000)
            return new CommentActionResult(false, "Nội dung comment tối đa 4000 ký tự.");

        var comment = await _db.TaskComments.FirstOrDefaultAsync(c => c.Id == commentId && c.TaskId == taskId, cancellationToken);
        if (comment is null)
            return new CommentActionResult(false, "Comment không tồn tại.");

        var canEdit = detail.IsPm || string.Equals(comment.UserId, actorUserId, StringComparison.Ordinal);
        if (!canEdit)
            return new CommentActionResult(false, "Bạn không có quyền chỉnh sửa comment này.");

        if (comment.IsDeleted)
            return new CommentActionResult(false, "Không thể chỉnh sửa comment đã bị xoá.");

        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var oldValue = comment.Content;
        comment.Content = content;
        comment.UpdatedAtUtc = now;

        var entry = new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actorUserId,
            Action = "Chỉnh sửa comment",
            OldValue = oldValue.Length > 500 ? oldValue[..500] : oldValue,
            NewValue = content.Length > 500 ? content[..500] : content,
            TimestampUtc = now
        };
        _db.TaskHistoryEntries.Add(entry);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is not null)
        {
            var actorDisplayName = await GetUserDisplayNameAsync(actorUserId, cancellationToken);
            await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
                "CommentUpdated",
                new
                {
                    taskId = taskId,
                    comment = new
                    {
                        id = comment.Id,
                        content = comment.Content,
                        updatedAtUtc = comment.UpdatedAtUtc
                    },
                    actor = new { userId = actorUserId, displayName = actorDisplayName }
                },
                cancellationToken);
            await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
                "HistoryAdded",
                new { taskId = taskId, entryId = entry.Id },
                cancellationToken);
        }

        return new CommentActionResult(true, null);
    }

    public async Task<CommentActionResult> DeleteCommentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        Guid commentId,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        if (!detail.CanManageComments)
            return new CommentActionResult(false, "Bạn không có quyền bình luận.");

        var comment = await _db.TaskComments.FirstOrDefaultAsync(c => c.Id == commentId && c.TaskId == taskId, cancellationToken);
        if (comment is null)
            return new CommentActionResult(false, "Comment không tồn tại.");

        var canDelete = detail.IsPm || string.Equals(comment.UserId, actorUserId, StringComparison.Ordinal);
        if (!canDelete)
            return new CommentActionResult(false, "Bạn không có quyền xoá comment này.");

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return new CommentActionResult(false, "Task không tồn tại.");

        var hasReplies = await _db.TaskComments.AnyAsync(c => c.ParentCommentId == commentId, cancellationToken);

        var now = DateTime.UtcNow;
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        string? newValue;
        var wasSoftDeleted = false;
        var wasHardDeleted = false;
        var oldContentForLog = comment.Content;
        if (hasReplies)
        {
            // Keep entry and mark deleted (RB-11).
            comment.IsDeleted = true;
            comment.Content = "[Bình luận đã bị xoá]";
            comment.UpdatedAtUtc = now;
            newValue = "[Bình luận đã bị xoá]";
            _db.TaskComments.Update(comment);
            wasSoftDeleted = true;
        }
        else
        {
            newValue = null;
            _db.TaskComments.Remove(comment);
            wasHardDeleted = true;
        }

        var action = "Xoá comment";
        if (detail.IsPm && !string.Equals(comment.UserId, actorUserId, StringComparison.Ordinal))
        {
            action = "PM xoá comment của Member";
            _logger.LogInformation(
                "PM {PmUserId} deleted comment {CommentId} of user {OwnerUserId} on task {TaskId} (soft={SoftDeleted}, hard={HardDeleted})",
                actorUserId,
                commentId,
                comment.UserId,
                taskId,
                wasSoftDeleted,
                wasHardDeleted);
        }

        var entry = new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actorUserId,
            Action = action,
            OldValue = string.IsNullOrWhiteSpace(oldContentForLog)
                ? null
                : (oldContentForLog.Length > 500 ? oldContentForLog[..500] : oldContentForLog),
            NewValue = newValue,
            TimestampUtc = now
        };
        _db.TaskHistoryEntries.Add(entry);

        await _db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);

        var actorDisplayName2 = await GetUserDisplayNameAsync(actorUserId, cancellationToken);
        await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
            "CommentDeleted",
            new
            {
                taskId = taskId,
                comment = new
                {
                    id = commentId,
                    isSoftDelete = hasReplies,
                    updatedAtUtc = now
                },
                actor = new { userId = actorUserId, displayName = actorDisplayName2 }
            },
            cancellationToken);
        await _hub.Clients.Group(task.ProjectId.ToString("D")).SendAsync(
            "HistoryAdded",
            new { taskId = taskId, entryId = entry.Id },
            cancellationToken);

        return new CommentActionResult(true, null);
    }

    public async Task<EvaluationUpdateResult> UpsertEvaluationAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        int score,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        return await EvaluateTaskAsync(taskId, actorUserId, workspaceId, score, comment, newLevel: null, cancellationToken);
    }

    public async Task<EvaluationUpdateResult> EvaluateTaskAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        int score,
        string? comment,
        MemberLevel? newLevel = null,
        CancellationToken cancellationToken = default)
    {
        var detail = await GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        if (!detail.IsPm)
            return new EvaluationUpdateResult(false, "Chỉ PM được đánh giá task.", null);

        if (score is < 1 or > 10)
            return new EvaluationUpdateResult(false, "Score phải nằm trong khoảng 1..10.", null);

        var normalizedComment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        if (normalizedComment is not null && normalizedComment.Length > 2000)
            return new EvaluationUpdateResult(false, "Comment tối đa 2000 ký tự.", null);

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
            return new EvaluationUpdateResult(false, "Task không tồn tại.", null);

        var project = await _db.Projects.FirstOrDefaultAsync(
            p => p.Id == task.ProjectId && p.WorkspaceId == workspaceId,
            cancellationToken);
        if (project is null)
            return new EvaluationUpdateResult(false, "Project không tồn tại trong workspace.", null);

        if (task.Status != TaskStatus.Done)
            return new EvaluationUpdateResult(false, "Task phải ở trạng thái Done để đánh giá.", null);

        var acceptedAssignment = await _db.TaskAssignments
            .Where(a => a.TaskId == taskId && a.Status == TaskAssignmentStatus.Accepted)
            .OrderByDescending(a => a.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (acceptedAssignment is null)
            return new EvaluationUpdateResult(false, "Thiếu assignee đã chấp nhận cho task.", null);

        var memberUserId = acceptedAssignment.AssigneeUserId;
        var now = DateTime.UtcNow;

        // UC-08: once evaluated & locked -> cannot evaluate again.
        var alreadyEvaluated = await _db.TaskEvaluations.AnyAsync(
            e => e.TaskId == taskId,
            cancellationToken);
        if (alreadyEvaluated)
            return new EvaluationUpdateResult(false, "Task đã được đánh giá và đã khoá. Không thể đánh giá lại.", null);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var eval = new TaskEvaluation
        {
            TaskId = taskId,
            PmUserId = actorUserId,
            Score = score,
            Comment = normalizedComment,
            EvaluatedAtUtc = now,
            IsLocked = true
        };
        _db.TaskEvaluations.Add(eval);

        var action = $"Evaluated task with score {score}";
        if (action.Length > 500) action = action[..500];

        var entry = new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actorUserId,
            Action = action,
            OldValue = null,
            NewValue = null,
            TimestampUtc = now
        };
        _db.TaskHistoryEntries.Add(entry);

        await _db.SaveChangesAsync(cancellationToken);

        await UpdateMemberProfileFromEvaluationsAsync(workspaceId, memberUserId, newLevel, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        // Realtime: notify assignee.
        await _notifications.CreateAndPushAsync(
            userId: memberUserId,
            type: NotificationType.TaskEvaluated,
            message: $"Task \"{task.Title}\" đã được PM đánh giá. Điểm: {score}/10.",
            workspaceId: workspaceId,
            projectId: project.Id,
            taskId: task.Id,
            redirectUrl: $"/Tasks/Details/{taskId}",
            cancellationToken: cancellationToken);

        // Realtime: refresh evaluation + history for viewers.
        await _hub.Clients.Group(project.Id.ToString("D")).SendAsync(
            "HistoryAdded",
            new { taskId = taskId, entryId = entry.Id },
            cancellationToken);

        await _hub.Clients.Group(project.Id.ToString("D")).SendAsync(
            "TaskEvaluated",
            new { taskId = taskId, evaluationId = eval.Id, score = score },
            cancellationToken);

        await _hub.Clients.Group(project.Id.ToString("D")).SendAsync(
            "TaskUpdated",
            new { taskId = taskId, newStatus = task.Status.ToString() },
            cancellationToken);

        var pm = await _db.Users.FirstOrDefaultAsync(u => u.Id == actorUserId, cancellationToken);
        var vm = new TaskEvaluationVm(
            eval.Id,
            eval.TaskId,
            eval.PmUserId,
            pm != null ? (pm.DisplayName ?? pm.Email ?? pm.UserName ?? pm.Id) : eval.PmUserId,
            pm?.AvatarUrl,
            eval.Score,
            eval.Comment,
            eval.EvaluatedAtUtc,
            eval.IsLocked);

        return new EvaluationUpdateResult(true, null, vm);
    }

    private async Task UpdateMemberProfileFromEvaluationsAsync(
        Guid workspaceId,
        string memberUserId,
        MemberLevel? overrideLevel,
        CancellationToken cancellationToken)
    {
        var scores = await _db.TaskEvaluations
            .Join(_db.Tasks, e => e.TaskId, t => t.Id, (e, t) => new { e, t })
            .Join(_db.Projects.Where(p => p.WorkspaceId == workspaceId), x => x.t.ProjectId, p => p.Id, (x, _) => x)
            .Where(x => _db.TaskAssignments.Any(a =>
                a.TaskId == x.t.Id &&
                a.AssigneeUserId == memberUserId &&
                a.Status == TaskAssignmentStatus.Accepted))
            .Select(x => x.e.Score)
            .ToListAsync(cancellationToken);

        var avgScore = scores.Count == 0 ? 0m : (decimal)scores.Average();

        // Completion rate = ratio of tasks that are Done and updated at/before due date.
        var memberTasksQuery = _db.TaskAssignments
            .Where(a => a.AssigneeUserId == memberUserId && a.Status == TaskAssignmentStatus.Accepted)
            .Join(_db.Tasks, a => a.TaskId, t => t.Id, (a, t) => t)
            .Join(_db.Projects.Where(p => p.WorkspaceId == workspaceId), t => t.ProjectId, p => p.Id, (t, _) => t);

        var totalAssigned = await memberTasksQuery.CountAsync(cancellationToken);

        var onTime = await memberTasksQuery.CountAsync(t =>
            t.Status == TaskStatus.Done &&
            t.DueDateUtc.HasValue &&
            t.UpdatedAtUtc <= t.DueDateUtc.Value,
            cancellationToken);

        var completionRate = totalAssigned == 0 ? 0m : (decimal)onTime / totalAssigned;

        var workload = await memberTasksQuery.CountAsync(t => t.Status == TaskStatus.InProgress, cancellationToken);

        var profile = await _db.MemberProfiles.FirstOrDefaultAsync(m => m.UserId == memberUserId, cancellationToken);
        if (profile is null)
        {
            profile = new MemberProfile { UserId = memberUserId };
            _db.MemberProfiles.Add(profile);
        }

        profile.AvgScore = avgScore;
        profile.CompletionRate = completionRate;
        profile.CurrentWorkload = workload;
        profile.Level = overrideLevel
            ?? (avgScore >= 8m ? MemberLevel.Senior : avgScore >= 6m ? MemberLevel.Mid : MemberLevel.Junior);
    }

    private async Task<string> GetUserDisplayNameAsync(string userId, CancellationToken cancellationToken)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        return user?.DisplayName ?? user?.Email ?? user?.UserName ?? userId;
    }
}

