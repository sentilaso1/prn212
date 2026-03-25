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

// UC-05: Only Member (not PM) can Accept/Reject tasks assigned to them.
[Authorize]
public sealed class AcceptRejectModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly ITaskAssignmentService _taskAssignmentService;

    public AcceptRejectModel(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService,
        ITaskAssignmentService taskAssignmentService)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
        _taskAssignmentService = taskAssignmentService;
    }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? SuccessToastMessage { get; set; }

    public bool ShowSuccessToast => !string.IsNullOrWhiteSpace(SuccessToastMessage);

    public TaskDetailsVm? Task { get; private set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public async Task OnGetAsync([FromRoute] Guid taskId, CancellationToken cancellationToken)
    {
        Input.TaskId = taskId;

        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Workspace không hợp lệ.";
            return;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ErrorMessage = "User không hợp lệ.";
            return;
        }

        // Reject access for PM.
        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId.Value &&
            m.UserId == userId &&
            m.Role == WorkspaceMemberRole.PM, cancellationToken);
        if (isPm)
        {
            ErrorMessage = "Chỉ Member được Accept/Reject task.";
            return;
        }

        // Must have pending assignment for this user.
        var assignment = await _db.TaskAssignments.FirstOrDefaultAsync(a =>
            a.TaskId == taskId &&
            a.AssigneeUserId == userId &&
            a.Status == TaskAssignmentStatus.Pending, cancellationToken);

        if (assignment is null)
        {
            ErrorMessage = "Task không tồn tại hoặc không được giao cho bạn (Pending).";
            return;
        }

        var task = await _db.Tasks.FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            ErrorMessage = "Task không thuộc workspace hiện tại.";
            return;
        }

        var project = await _db.Projects.FirstOrDefaultAsync(p =>
            p.Id == task.ProjectId && p.WorkspaceId == workspaceId.Value, cancellationToken);

        if (project is null)
        {
            ErrorMessage = "Project không hợp lệ trong workspace hiện tại.";
            return;
        }

        Task = new TaskDetailsVm(
            taskId: task.Id,
            title: task.Title,
            projectId: task.ProjectId,
            projectName: project.Name,
            dueDateUtc: task.DueDateUtc,
            priority: task.Priority);
    }

    public async Task<IActionResult> OnPostAcceptAsync(CancellationToken cancellationToken)
    {
        if (Input.TaskId == Guid.Empty)
        {
            ErrorMessage = "TaskId không hợp lệ.";
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ErrorMessage = "User không hợp lệ.";
            return Page();
        }

        var result = await _taskAssignmentService.AcceptAsync(
            Input.TaskId,
            actorUserId: userId,
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể Accept task.";
            await OnGetAsync(Input.TaskId, cancellationToken);
            return Page();
        }

        SuccessToastMessage = "Accepted task thành công";

        // UC-05: redirect to Kanban board of project.
        if (Task is not null)
            return LocalRedirect($"/board?projectId={Task.ProjectId}");

        // Fallback: reload to get projectId.
        await OnGetAsync(Input.TaskId, cancellationToken);
        return Task is not null
            ? LocalRedirect($"/board?projectId={Task.ProjectId}")
            : RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(CancellationToken cancellationToken)
    {
        if (Input.TaskId == Guid.Empty)
        {
            ErrorMessage = "TaskId không hợp lệ.";
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ErrorMessage = "User không hợp lệ.";
            return Page();
        }

        var result = await _taskAssignmentService.RejectAsync(
            Input.TaskId,
            actorUserId: userId,
            rejectReason: Input.RejectReason ?? string.Empty,
            cancellationToken: cancellationToken);

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể Reject task.";
            await OnGetAsync(Input.TaskId, cancellationToken);
            return Page();
        }

        SuccessToastMessage = "Rejected task thành công";

        if (Task is not null)
            return LocalRedirect($"/board?projectId={Task.ProjectId}");

        await OnGetAsync(Input.TaskId, cancellationToken);
        return Task is not null
            ? LocalRedirect($"/board?projectId={Task.ProjectId}")
            : RedirectToPage();
    }

    public sealed class TaskDetailsVm
    {
        public TaskDetailsVm(
            Guid taskId,
            string title,
            Guid projectId,
            string projectName,
            DateTime? dueDateUtc,
            TaskPriority priority)
        {
            TaskId = taskId;
            Title = title;
            ProjectId = projectId;
            ProjectName = projectName;
            DueDateUtc = dueDateUtc;
            Priority = priority;
        }

        public Guid TaskId { get; }
        public string Title { get; }
        public Guid ProjectId { get; }
        public string ProjectName { get; }
        public DateTime? DueDateUtc { get; }
        public TaskPriority Priority { get; }
    }

    public sealed class InputModel
    {
        public Guid TaskId { get; set; }

        [StringLength(2000)]
        public string? RejectReason { get; set; }
    }
}

