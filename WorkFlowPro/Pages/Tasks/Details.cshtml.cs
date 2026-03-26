using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Tasks;

using TaskStatus = WorkFlowPro.Data.TaskStatus;

[Authorize]
public sealed class DetailsModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly ITaskService _taskService;

    public DetailsModel(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService,
        ITaskService taskService)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
        _taskService = taskService;
    }

    public string? ErrorMessage { get; private set; }

    public TaskOverviewVm? OverviewVm { get; private set; }
    public AttachmentsVm? AttachmentsVm { get; private set; }
    public CommentsVm? CommentsVm { get; private set; }
    public ActivityLogVm? ActivityLogVm { get; private set; }
    public IReadOnlyList<TaskEvaluationVm> EvaluationHistoryVm { get; private set; } = Array.Empty<TaskEvaluationVm>();

    public Guid TaskId { get; private set; }
    public Guid ProjectId { get; private set; }
    public string ActorUserId { get; private set; } = string.Empty;
    public bool IsPm => OverviewVm?.detail.IsPm ?? false;
    public Guid? CurrentWorkspaceId => _currentWorkspaceService.CurrentWorkspaceId;

    [BindProperty]
    public UpdateTaskInputModel UpdateTask { get; set; } = new();

    public sealed class UpdateTaskInputModel
    {
        [StringLength(250)]
        public string? Title { get; set; }

        [StringLength(4000)]
        public string? Description { get; set; }

        public TaskPriority? Priority { get; set; }

        [DataType(DataType.Date)]
        public DateTime? DueDateUtc { get; set; }

        // null: keep / do not change, "" : remove assignee
        public string? NewAssigneeUserId { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(Guid taskId, CancellationToken cancellationToken)
    {
        TaskId = taskId;

        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
        {
            ErrorMessage = "Workspace hoặc user không hợp lệ.";
            return Page();
        }

        ActorUserId = actorUserId;

        try
        {
            await LoadAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return Page();
        }
    }

    private async Task LoadAsync(
        Guid taskId,
        Guid workspaceId,
        string actorUserId,
        CancellationToken cancellationToken)
    {
        ActorUserId = actorUserId;
        var detail = await _taskService.GetTaskDetailAsync(taskId, actorUserId, workspaceId, cancellationToken);
        ProjectId = detail.ProjectId;

        var assigneeOptions = await GetAssigneeOptionsAsync(workspaceId, cancellationToken);

        var attachments = await _taskService.GetTaskAttachmentsAsync(taskId, actorUserId, workspaceId, cancellationToken);
        var comments = await _taskService.GetTaskCommentsAsync(taskId, actorUserId, workspaceId, cancellationToken);
        var history = await _taskService.GetTaskHistoryAsync(taskId, actorUserId, workspaceId, cancellationToken);
        var evaluations = await _taskService.GetTaskEvaluationsAsync(taskId, actorUserId, workspaceId, cancellationToken);

        OverviewVm = new TaskOverviewVm(
            detail: detail,
            assigneeOptions: assigneeOptions,
            WorkspaceId: CurrentWorkspaceId);

        AttachmentsVm = new AttachmentsVm(
            detail: detail,
            items: attachments);

        CommentsVm = new CommentsVm(
            detail: detail,
            actorUserId: actorUserId,
            items: comments);

        ActivityLogVm = new ActivityLogVm(
            items: history);

        EvaluationHistoryVm = evaluations;

        // Initialize bound inputs with current values for better UX (PM edit).
        UpdateTask.Title = detail.Title;
        UpdateTask.Description = detail.Description;
        UpdateTask.Priority = detail.Priority;
        UpdateTask.DueDateUtc = detail.DueDateUtc;
        UpdateTask.NewAssigneeUserId = detail.AssigneeUserId;
    }

    private async Task<IReadOnlyList<AssigneeOptionVm>> GetAssigneeOptionsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var raw = await (
            from wm in _db.WorkspaceMembers
            where wm.WorkspaceId == workspaceId && wm.Role != WorkspaceMemberRole.PM
            join u in _db.Users on wm.UserId equals u.Id
            select new { u.Id, u.DisplayName, u.Email, u.UserName })
            .ToListAsync(cancellationToken);

        return raw
            .Select(r => new AssigneeOptionVm(r.Id, r.DisplayName ?? r.Email ?? r.UserName ?? r.Id))
            .OrderBy(o => o.DisplayName)
            .ToList();
    }

    private bool IsAjaxRequest() =>
        string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

    private async Task<IActionResult> ReloadPageAsync(Guid taskId, Guid workspaceId, string actorUserId, CancellationToken cancellationToken)
    {
        await LoadAsync(taskId, workspaceId, actorUserId, cancellationToken);
        return Page();
    }

    // ────────────────────────────────────────────────────────────────
    // POST: Update core task / assignee (PM), description (PM/assignee)
    // ────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostUpdateTaskAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        try
        {
            var request = new TaskUpdateRequest(
                Title: UpdateTask.Title,
                Description: UpdateTask.Description,
                Priority: UpdateTask.Priority,
                DueDateUtc: UpdateTask.DueDateUtc,
                NewAssigneeUserId: UpdateTask.NewAssigneeUserId);

            // Basic validation for due-date if provided.
            if (request.DueDateUtc.HasValue && request.DueDateUtc.Value.Date <= DateTime.UtcNow.Date)
            {
                ModelState.AddModelError(nameof(UpdateTask.DueDateUtc), "DueDateUtc phải sau hôm nay.");
            }

            if (!ModelState.IsValid)
            {
                ErrorMessage = "Dữ liệu không hợp lệ.";
                if (IsAjaxRequest())
                    return new JsonResult(new { success = false, error = ErrorMessage }) { StatusCode = 400 };

                return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
            }

            var result = await _taskService.UpdateTaskAsync(taskId, actorUserId, workspaceId.Value, request, cancellationToken);
            if (!result.Success)
            {
                ErrorMessage = result.ErrorMessage ?? "Cập nhật task thất bại.";
                if (IsAjaxRequest())
                    return new JsonResult(new { success = false, error = ErrorMessage }) { StatusCode = 400 };

                return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
            }

            if (IsAjaxRequest())
                return new JsonResult(new { success = true });

            return LocalRedirect($"/Tasks/Details/{taskId}");
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            if (IsAjaxRequest())
                return new JsonResult(new { success = false, error = ErrorMessage }) { StatusCode = 400 };

            return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }
    }

    // ────────────────────────────────────────────────────────────────
    // POST: Upload attachment
    // ────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostAddAttachmentAsync(Guid taskId, IFormFile? file, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        if (file is null)
        {
            if (IsAjaxRequest())
                return new JsonResult(new { success = false, error = "File không hợp lệ." }) { StatusCode = 400 };

            ErrorMessage = "File không hợp lệ.";
            return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }

        var result = await _taskService.AddAttachmentAsync(taskId, actorUserId, workspaceId.Value, file, cancellationToken);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Upload attachment thất bại.";
            if (IsAjaxRequest())
                return new JsonResult(new { success = false, error = ErrorMessage }) { StatusCode = 400 };

            return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }

        if (IsAjaxRequest())
            return new JsonResult(new { success = true, attachmentId = result.Attachment?.Id });

        return LocalRedirect($"/Tasks/Details/{taskId}");
    }

    public async Task<IActionResult> OnPostDeleteAttachmentAsync(Guid taskId, Guid attachmentId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        var result = await _taskService.DeleteAttachmentAsync(taskId, actorUserId, workspaceId.Value, attachmentId, cancellationToken);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể xoá attachment.";
            if (IsAjaxRequest())
                return new JsonResult(new { success = false, error = ErrorMessage }) { StatusCode = 400 };

            return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }

        if (IsAjaxRequest())
            return new JsonResult(new { success = true });

        return LocalRedirect($"/Tasks/Details/{taskId}");
    }

    // ────────────────────────────────────────────────────────────────
    // POST: Comments
    // ────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostAddCommentAsync(Guid taskId, string content, Guid? parentCommentId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        var result = await _taskService.AddCommentAsync(taskId, actorUserId, workspaceId.Value, content, parentCommentId, cancellationToken);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể thêm comment.";
            if (IsAjaxRequest())
                return new JsonResult(new { success = false, error = ErrorMessage }) { StatusCode = 400 };

            return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }

        if (IsAjaxRequest())
            return new JsonResult(new { success = true });

        return LocalRedirect($"/Tasks/Details/{taskId}");
    }

    public async Task<IActionResult> OnPostEditCommentAsync(Guid taskId, Guid commentId, string content, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        var result = await _taskService.EditCommentAsync(taskId, actorUserId, workspaceId.Value, commentId, content, cancellationToken);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể sửa comment.";
            if (IsAjaxRequest())
                return new JsonResult(new { success = false, error = ErrorMessage }) { StatusCode = 400 };

            return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }

        if (IsAjaxRequest())
            return new JsonResult(new { success = true });

        return LocalRedirect($"/Tasks/Details/{taskId}");
    }

    public async Task<IActionResult> OnPostDeleteCommentAsync(Guid taskId, Guid commentId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        var result = await _taskService.DeleteCommentAsync(taskId, actorUserId, workspaceId.Value, commentId, cancellationToken);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể xoá comment.";
            if (IsAjaxRequest())
                return new JsonResult(new { success = false, error = ErrorMessage }) { StatusCode = 400 };

            return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }

        if (IsAjaxRequest())
            return new JsonResult(new { success = true });

        return LocalRedirect($"/Tasks/Details/{taskId}");
    }

    // ────────────────────────────────────────────────────────────────
    // POST: Evaluation (PM only)
    // ────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnPostUpsertEvaluationAsync(Guid taskId, int score, string? comment, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        var result = await _taskService.EvaluateTaskAsync(taskId, actorUserId, workspaceId.Value, score, comment, cancellationToken);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể đánh giá task.";
            if (IsAjaxRequest())
                return new JsonResult(new { success = false, error = ErrorMessage }) { StatusCode = 400 };

            return await ReloadPageAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }

        if (IsAjaxRequest())
            return new JsonResult(new { success = true, score = result.Evaluation?.Score });

        return LocalRedirect($"/Tasks/Details/{taskId}");
    }

    // ────────────────────────────────────────────────────────────────
    // JSON endpoints for SignalR refresh (no full reload)
    // ────────────────────────────────────────────────────────────────
    public async Task<JsonResult> OnGetOverviewJsonAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return new JsonResult(new { error = "unauthorized" }) { StatusCode = 401 };

        var detail = await _taskService.GetTaskDetailAsync(taskId, actorUserId, workspaceId.Value, cancellationToken);
        return new JsonResult(new
        {
            detail.TaskId,
            detail.ProjectName,
            detail.Title,
            detail.Description,
            detail.Priority,
            detail.DueDateUtc,
            detail.Status,
            detail.AssigneeDisplayName,
            detail.AssigneeAvatarUrl,
            detail.CreatedByDisplayName,
            detail.CreatedAtUtc,
            detail.UpdatedAtUtc
        });
    }

    public async Task<JsonResult> OnGetAttachmentsJsonAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return new JsonResult(new { error = "unauthorized" }) { StatusCode = 401 };

        var list = await _taskService.GetTaskAttachmentsAsync(taskId, actorUserId, workspaceId.Value, cancellationToken);
        return new JsonResult(list);
    }

    public async Task<JsonResult> OnGetCommentsJsonAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return new JsonResult(new { error = "unauthorized" }) { StatusCode = 401 };

        var list = await _taskService.GetTaskCommentsAsync(taskId, actorUserId, workspaceId.Value, cancellationToken);
        return new JsonResult(list);
    }

    public async Task<JsonResult> OnGetHistoryJsonAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return new JsonResult(new { error = "unauthorized" }) { StatusCode = 401 };

        var list = await _taskService.GetTaskHistoryAsync(taskId, actorUserId, workspaceId.Value, cancellationToken);
        return new JsonResult(list);
    }

    public async Task<JsonResult> OnGetEvaluationJsonAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return new JsonResult(new { error = "unauthorized" }) { StatusCode = 401 };

        var eval = await _taskService.GetTaskEvaluationAsync(taskId, actorUserId, workspaceId.Value, cancellationToken);
        return new JsonResult(eval);
    }

    // ────────────────────────────────────────────────────────────────
    // Partial HTML endpoints for SignalR refresh (no full reload)
    // ────────────────────────────────────────────────────────────────
    public async Task<IActionResult> OnGetOverviewPartialAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        try
        {
            await LoadAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return new PartialViewResult
        {
            ViewName = "_TaskOverview",
            ViewData = new ViewDataDictionary<TaskOverviewVm>(ViewData, OverviewVm!)
        };
    }

    public async Task<IActionResult> OnGetAttachmentsPartialAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        try
        {
            await LoadAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return new PartialViewResult
        {
            ViewName = "_Attachments",
            ViewData = new ViewDataDictionary<AttachmentsVm>(ViewData, AttachmentsVm!)
        };
    }

    public async Task<IActionResult> OnGetCommentsPartialAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        try
        {
            await LoadAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return new PartialViewResult
        {
            ViewName = "_Comments",
            ViewData = new ViewDataDictionary<CommentsVm>(ViewData, CommentsVm!)
        };
    }

    public async Task<IActionResult> OnGetActivityLogPartialAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        try
        {
            await LoadAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        return new PartialViewResult
        {
            ViewName = "_ActivityLog",
            ViewData = new ViewDataDictionary<ActivityLogVm>(ViewData, ActivityLogVm!)
        };
    }

    public async Task<IActionResult> OnGetEvaluationPartialAsync(Guid taskId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (workspaceId is null || string.IsNullOrWhiteSpace(actorUserId))
            return Unauthorized();

        try
        {
            await LoadAsync(taskId, workspaceId.Value, actorUserId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        var vm = new EvaluationSectionVm(
            isPm: OverviewVm!.detail.IsPm,
            taskId: taskId,
            status: OverviewVm!.detail.Status,
            evaluations: EvaluationHistoryVm);

        return new PartialViewResult
        {
            ViewName = "_TaskEvaluation",
            ViewData = new ViewDataDictionary<EvaluationSectionVm>(ViewData, vm)
        };
    }
}

public sealed record AssigneeOptionVm(string UserId, string DisplayName);

public sealed record TaskOverviewVm(
    TaskDetailVm detail,
    IReadOnlyList<AssigneeOptionVm> assigneeOptions,
    Guid? WorkspaceId = null);

public sealed record AttachmentsVm(
    TaskDetailVm detail,
    IReadOnlyList<AttachmentVm> items);

public sealed record CommentsVm(
    TaskDetailVm detail,
    string actorUserId,
    IReadOnlyList<CommentNodeVm> items);

public sealed record ActivityLogVm(
    IReadOnlyList<HistoryEntryVm> items);

public sealed record CommentNodeRenderVm(CommentNodeVm Node, string ActorUserId, bool CanManageComments);

public sealed record EvaluationSectionVm(
    bool isPm,
    Guid taskId,
    TaskStatus status,
    IReadOnlyList<TaskEvaluationVm> evaluations);

