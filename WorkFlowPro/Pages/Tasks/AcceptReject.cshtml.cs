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

using TaskItemStatus = WorkFlowPro.Data.TaskStatus;

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

    public async Task<IActionResult> OnGetAsync([FromRoute] Guid taskId, CancellationToken cancellationToken)
    {
        Input.TaskId = taskId;

        var currentWs = _currentWorkspaceService.CurrentWorkspaceId;
        if (currentWs is null)
        {
            ErrorMessage = "Workspace không hợp lệ.";
            return Page();
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ErrorMessage = "User không hợp lệ.";
            return Page();
        }

        var task = await _db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId, cancellationToken);
        if (task is null)
        {
            ErrorMessage = "Không tìm thấy task.";
            return Page();
        }

        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == task.ProjectId, cancellationToken);
        if (project is null)
        {
            ErrorMessage = "Không tìm thấy dự án của task.";
            return Page();
        }

        var inProjectWorkspace = await _db.WorkspaceMembers.AnyAsync(
            m => m.WorkspaceId == project.WorkspaceId && m.UserId == userId,
            cancellationToken);
        if (!inProjectWorkspace)
        {
            ErrorMessage = "Bạn không thuộc đơn vị chứa task này.";
            return Page();
        }

        if (currentWs.Value != project.WorkspaceId)
        {
            ErrorMessage =
                $"Task thuộc dự án \"{project.Name}\" ở đơn vị khác với đơn vị đang chọn. " +
                "Hãy đổi đơn vị (menu góc phải) đúng workspace rồi mở lại link (thông báo / Accept).";
            return Page();
        }

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == currentWs.Value &&
            m.UserId == userId &&
            m.Role == WorkspaceMemberRole.PM, cancellationToken);
        if (isPm)
        {
            ErrorMessage = "Chỉ Member được Accept/Reject task.";
            return Page();
        }

        var assignment = await _db.TaskAssignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TaskId == taskId && a.AssigneeUserId == userId, cancellationToken);

        if (assignment is null)
        {
            ErrorMessage = "Bạn không được giao task này.";
            return Page();
        }

        if (assignment.Status == TaskAssignmentStatus.Accepted)
            return LocalRedirect($"/Tasks/Details/{taskId:D}");

        if (assignment.Status == TaskAssignmentStatus.Rejected)
        {
            ErrorMessage = "Bạn đã từ chối task này trước đó.";
            return Page();
        }

        if (assignment.Status != TaskAssignmentStatus.Pending)
        {
            ErrorMessage = "Trạng thái giao việc không hợp lệ để Accept/Reject.";
            return Page();
        }

        if (task.Status != TaskItemStatus.Pending)
        {
            ErrorMessage = "Task không còn ở trạng thái chờ bạn chấp nhận (có thể PM hoặc hệ thống đã đổi trạng thái).";
            return Page();
        }

        Task = new TaskDetailsVm(
            taskId: task.Id,
            title: task.Title,
            projectId: task.ProjectId,
            projectName: project.Name,
            dueDateUtc: task.DueDateUtc,
            priority: task.Priority);
        return Page();
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
            return await OnGetAsync(Input.TaskId, cancellationToken);
        }

        SuccessToastMessage = "Đã chấp nhận task.";
        var projectId = await _db.Tasks.AsNoTracking()
            .Where(t => t.Id == Input.TaskId)
            .Select(t => t.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
        if (projectId == Guid.Empty)
            return RedirectToPage("/Tasks/MyTasks/Pending");
        return LocalRedirect($"/board?projectId={projectId:D}");
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
            return await OnGetAsync(Input.TaskId, cancellationToken);
        }

        SuccessToastMessage = "Đã từ chối task.";
        var projectId = await _db.Tasks.AsNoTracking()
            .Where(t => t.Id == Input.TaskId)
            .Select(t => t.ProjectId)
            .FirstOrDefaultAsync(cancellationToken);
        if (projectId == Guid.Empty)
            return RedirectToPage("/Tasks/MyTasks/Pending");
        return LocalRedirect($"/board?projectId={projectId:D}");
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
