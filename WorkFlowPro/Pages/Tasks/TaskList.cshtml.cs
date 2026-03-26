using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Pages.Tasks;

[Authorize]
public sealed class TaskListModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly ITaskService _taskService;

    public TaskListModel(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService,
        ITaskService taskService)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
        _taskService = taskService;
    }

    public string? ErrorMessage { get; private set; }

    public Guid ProjectId { get; private set; }
    public string ProjectName { get; private set; } = string.Empty;

    public string CurrentUserId { get; private set; } = string.Empty;
    public bool IsPm { get; private set; }
    public Guid? CurrentWorkspaceId => _currentWorkspaceService.CurrentWorkspaceId;

    public IReadOnlyList<TaskCardVm> Tasks { get; private set; } = Array.Empty<TaskCardVm>();

    public TaskFiltersVm Filters { get; private set; } = default!;

    public async Task OnGetAsync([FromQuery] Guid projectId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Workspace không hợp lệ.";
            return;
        }

        if (projectId == Guid.Empty)
        {
            ErrorMessage = "projectId là bắt buộc.";
            return;
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ErrorMessage = "User không hợp lệ.";
            return;
        }

        var isMember = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId.Value && m.UserId == userId, cancellationToken);
        if (!isMember)
        {
            ErrorMessage = "Bạn không có quyền xem task trong workspace này.";
            return;
        }

        var project = await _db.Projects.FirstOrDefaultAsync(p =>
            p.Id == projectId && p.WorkspaceId == workspaceId.Value, cancellationToken);
        if (project is null)
        {
            ErrorMessage = "Project không tồn tại trong workspace hiện tại.";
            return;
        }

        ProjectId = project.Id;
        ProjectName = project.Name;
        CurrentUserId = userId;

        IsPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId.Value &&
            m.UserId == userId &&
            m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        var sessionJson = HttpContext.Session.GetString(TaskFilterSession.KeyForProject(projectId));
        var criteria = TaskFilterSession.Deserialize(sessionJson);

        var members = await _taskService.GetWorkspaceMemberFilterOptionsAsync(workspaceId.Value, cancellationToken);

        try
        {
            var result = await _taskService.GetFilteredTaskListAsync(
                projectId,
                workspaceId.Value,
                userId,
                criteria,
                cancellationToken);

            Tasks = result.Tasks;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        Filters = new TaskFiltersVm
        {
            ProjectId = projectId,
            ViewContext = "list",
            Criteria = criteria,
            WorkspaceMembers = members,
            IsPm = IsPm
        };
    }
}
