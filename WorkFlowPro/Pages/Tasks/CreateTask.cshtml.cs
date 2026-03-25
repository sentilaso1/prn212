using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Tasks;

using TaskStatus = WorkFlowPro.Data.TaskStatus;

[Authorize(Policy = "IsPM")]
public sealed class CreateTaskModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly ITaskService _taskService;
    private readonly ITaskHistoryService _history;
    private readonly INotificationService _notifications;

    public CreateTaskModel(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService,
        ITaskService taskService,
        ITaskHistoryService history,
        INotificationService notifications)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
        _taskService = taskService;
        _history = history;
        _notifications = notifications;
    }

    [TempData]
    public string? SuccessToastMessage { get; set; }

    public bool ShowSuccessToast => !string.IsNullOrWhiteSpace(SuccessToastMessage);

    public string? ErrorMessage { get; private set; }

    /// <summary>UC-14: SignalR JoinWorkspace + làm mới gợi ý khi profile member đổi.</summary>
    public Guid? CurrentWorkspaceId { get; private set; }

    public List<SuggestedAssigneeVm> SuggestedAssignees { get; private set; } = new();

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync([FromQuery] Guid projectId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Workspace hiện tại không hợp lệ.";
            return;
        }

        CurrentWorkspaceId = workspaceId.Value;

        if (projectId == Guid.Empty)
        {
            ErrorMessage = "ProjectId không hợp lệ.";
            return;
        }

        // Requirement: ProjectId phải thuộc CurrentWorkspace.
        var projectOk = await _db.Projects.AnyAsync(p =>
            p.Id == projectId && p.WorkspaceId == workspaceId.Value, cancellationToken);

        if (!projectOk)
        {
            Forbid();
            return;
        }

        Input.ProjectId = projectId;
        Input.Priority = TaskPriority.Medium;
        Input.AssigneeUserId = null;

        SuggestedAssignees = (await _taskService.GetSuggestedAssigneesAsync(
            workspaceId.Value,
            Input.Priority,
            take: 5,
            cancellationToken: cancellationToken)).ToList();
    }

    public async Task<IActionResult> OnPostCreateAsync(CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        var project = await ValidateAndLoadProjectOrAddErrorAsync(workspaceId.Value, cancellationToken);
        if (project is null)
            return await ReRenderWithSuggestionsAsync(workspaceId.Value, cancellationToken);

        if (!TryValidateTitleAndDueDate())
            return await ReRenderWithSuggestionsAsync(workspaceId.Value, cancellationToken, project);

        var assigneeUserId = NormalizeAssignee(Input.AssigneeUserId);

        if (assigneeUserId is not null)
        {
            var allowed = await _db.WorkspaceMembers.AnyAsync(m =>
                m.WorkspaceId == workspaceId.Value &&
                m.UserId == assigneeUserId &&
                m.Role != WorkspaceMemberRole.PM, cancellationToken);

            if (!allowed)
            {
                ModelState.AddModelError(nameof(Input.AssigneeUserId), "Assignee không hợp lệ.");
                return await ReRenderWithSuggestionsAsync(workspaceId.Value, cancellationToken, project);
            }
        }

        if (!ModelState.IsValid)
            return await ReRenderWithSuggestionsAsync(workspaceId.Value, cancellationToken, project);

        // Create TaskItem (+ optional TaskAssignment) atomically.
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var now = DateTime.UtcNow;

        var task = new TaskItem
        {
            ProjectId = project.Id,
            Title = Input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
            DueDateUtc = Input.DueDateUtc,
            Priority = Input.Priority,
            Status = assigneeUserId is null ? TaskStatus.Unassigned : TaskStatus.Pending,
            CreatedByUserId = actorUserId,
            UpdatedAtUtc = now
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken);

        if (assigneeUserId is not null)
        {
            var assignment = new TaskAssignment
            {
                TaskId = task.Id,
                AssigneeUserId = assigneeUserId,
                Status = TaskAssignmentStatus.Pending
            };
            _db.TaskAssignments.Add(assignment);
            await _db.SaveChangesAsync(cancellationToken);
        }

        // Create history entry (UC-04)
        await _history.LogAsync(
            task.Id,
            User,
            action: "Tạo task",
            oldValue: null,
            newValue: task.Title,
            cancellationToken: cancellationToken);

        await tx.CommitAsync(cancellationToken);

        // UC-04: If assignee is chosen -> send realtime notification to assignee.
        if (assigneeUserId is not null)
        {
            await _notifications.CreateAndPushAsync(
                userId: assigneeUserId,
                type: NotificationType.TaskAssignedPending,
                message: $"Bạn được giao task \"{task.Title}\".",
                workspaceId: workspaceId.Value,
                projectId: project.Id,
                taskId: task.Id,
                // UC-05: Member nhận task qua notification -> mở trang accept/reject.
                redirectUrl: $"/Tasks/AcceptReject/{task.Id}",
                cancellationToken: cancellationToken);
        }

        SuccessToastMessage = "Tạo task thành công";
        // Redirect POST -> GET để tránh double-submit.
        return LocalRedirect($"/Tasks/Create?projectId={project.Id}");
    }

    public async Task<IActionResult> OnPostCreateWithSuggestionAsync(CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        var project = await ValidateAndLoadProjectOrAddErrorAsync(workspaceId.Value, cancellationToken);
        if (project is null)
            return await ReRenderWithSuggestionsAsync(workspaceId.Value, cancellationToken);

        if (!TryValidateTitleAndDueDate())
            return await ReRenderWithSuggestionsAsync(workspaceId.Value, cancellationToken, project);

        // Compute suggestions based on current Priority.
        var suggestions = await _taskService.GetSuggestedAssigneesAsync(
            workspaceId.Value,
            Input.Priority,
            take: 5,
            cancellationToken: cancellationToken);

        SuggestedAssignees = suggestions.ToList();

        var assigneeUserId = NormalizeAssignee(Input.AssigneeUserId);

        if (assigneeUserId is not null)
        {
            var inSuggestions = suggestions.Any(s => s.UserId == assigneeUserId);
            if (!inSuggestions)
                ModelState.AddModelError(nameof(Input.AssigneeUserId), "Assignee không nằm trong danh sách gợi ý.");
        }

        if (!ModelState.IsValid)
            return await ReRenderWithSuggestionsAsync(workspaceId.Value, cancellationToken, project, suggestions);

        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var now = DateTime.UtcNow;

        await using var tx2 = await _db.Database.BeginTransactionAsync(cancellationToken);

        var task = new TaskItem
        {
            ProjectId = project.Id,
            Title = Input.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(Input.Description) ? null : Input.Description.Trim(),
            DueDateUtc = Input.DueDateUtc,
            Priority = Input.Priority,
            Status = assigneeUserId is null ? TaskStatus.Unassigned : TaskStatus.Pending,
            CreatedByUserId = actorUserId,
            UpdatedAtUtc = now
        };

        _db.Tasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken);

        await _history.LogAsync(
            task.Id,
            User,
            action: "Tạo task",
            oldValue: null,
            newValue: task.Title,
            cancellationToken: cancellationToken);

        if (assigneeUserId is not null)
        {
            var assignment = new TaskAssignment
            {
                TaskId = task.Id,
                AssigneeUserId = assigneeUserId,
                Status = TaskAssignmentStatus.Pending
            };
            _db.TaskAssignments.Add(assignment);
            await _db.SaveChangesAsync(cancellationToken);
        }

        await tx2.CommitAsync(cancellationToken);

        // UC-04: If assignee is chosen -> send realtime notification to assignee.
        if (assigneeUserId is not null)
        {
            await _notifications.CreateAndPushAsync(
                userId: assigneeUserId,
                type: NotificationType.TaskAssignedPending,
                message: $"Bạn được giao task \"{task.Title}\".",
                workspaceId: workspaceId.Value,
                projectId: project.Id,
                taskId: task.Id,
                // UC-05: Member nhận task qua notification -> mở trang accept/reject.
                redirectUrl: $"/Tasks/AcceptReject/{task.Id}",
                cancellationToken: cancellationToken);
        }

        SuccessToastMessage = "Tạo task thành công";
        return LocalRedirect($"/Tasks/Create?projectId={project.Id}");
    }

    private bool TryValidateTitleAndDueDate()
    {
        // Title không rỗng (explicit requirement).
        if (string.IsNullOrWhiteSpace(Input.Title))
            ModelState.AddModelError(nameof(Input.Title), "Title là bắt buộc.");

        if (Input.DueDateUtc is DateTime due)
        {
            // DueDate phải sau hôm nay.
            if (due.Date <= DateTime.UtcNow.Date)
                ModelState.AddModelError(nameof(Input.DueDateUtc), "DueDateUtc phải sau hôm nay.");
        }

        // Trim here to avoid inconsistencies in later persistence.
        if (!string.IsNullOrWhiteSpace(Input.Title))
            Input.Title = Input.Title.Trim();

        return ModelState.IsValid;
    }

    private async Task<Project?> ValidateAndLoadProjectOrAddErrorAsync(
        Guid workspaceId,
        CancellationToken cancellationToken)
    {
        if (Input.ProjectId == Guid.Empty)
        {
            ModelState.AddModelError(nameof(Input.ProjectId), "ProjectId là bắt buộc.");
            return null;
        }

        var project = await _db.Projects.FirstOrDefaultAsync(
            p => p.Id == Input.ProjectId && p.WorkspaceId == workspaceId,
            cancellationToken);

        if (project is null)
            ModelState.AddModelError(nameof(Input.ProjectId), "Project không tồn tại trong workspace hiện tại.");

        return project;
    }

    private static string? NormalizeAssignee(string? assigneeUserId)
    {
        if (string.IsNullOrWhiteSpace(assigneeUserId))
            return null;
        return assigneeUserId.Trim();
    }

    private async Task<IActionResult> ReRenderWithSuggestionsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken,
        Project? project = null,
        IReadOnlyList<SuggestedAssigneeVm>? suggestions = null)
    {
        CurrentWorkspaceId = workspaceId;

        if (project is null)
        {
            project = await _db.Projects.FirstOrDefaultAsync(
                p => p.Id == Input.ProjectId && p.WorkspaceId == workspaceId,
                cancellationToken);
        }

        // Always compute suggestions when returning the page so the UI is consistent.
        if (suggestions is null)
            SuggestedAssignees = (await _taskService.GetSuggestedAssigneesAsync(
                workspaceId,
                Input.Priority,
                take: 5,
                cancellationToken: cancellationToken)).ToList();
        else
            SuggestedAssignees = suggestions.ToList();

        return Page();
    }

    public sealed class InputModel
    {
        [Required]
        public Guid ProjectId { get; set; }

        [Required]
        [StringLength(250, MinimumLength = 1)]
        public string Title { get; set; } = default!;

        [StringLength(4000)]
        public string? Description { get; set; }

        [Required]
        public TaskPriority Priority { get; set; } = TaskPriority.Medium;

        [DataType(DataType.Date)]
        public DateTime? DueDateUtc { get; set; }

        public string? AssigneeUserId { get; set; }
    }
}

