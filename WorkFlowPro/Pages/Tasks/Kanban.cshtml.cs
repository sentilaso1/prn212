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
public sealed class KanbanModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly ITaskService _taskService;

    public KanbanModel(
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

    public FilteredKanbanTasksResult Board { get; private set; } = new();

    public TaskFiltersVm Filters { get; private set; } = default!;

    public async Task OnGetAsync([FromQuery] Guid projectId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ErrorMessage = "User không hợp lệ.";
            return;
        }

        if (projectId == Guid.Empty)
        {
            ErrorMessage = "projectId là bắt buộc.";
            return;
        }

        // 1. Tìm dự án không dùng filter
        var project = await _db.Projects.IgnoreQueryFilters().FirstOrDefaultAsync(p =>
            p.Id == projectId, cancellationToken);

        if (project is null)
        {
            ErrorMessage = "Dự án không tồn tại.";
            return;
        }

        // 2. Kiểm tra xem user có thuộc workspace của dự án không
        var membership = await _db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == project.WorkspaceId && m.UserId == userId, cancellationToken);

        if (membership is null)
        {
            ErrorMessage = "Bạn không có quyền truy cập dự án này.";
            return;
        }

        if (project.Status != ProjectStatus.Active && project.Status != ProjectStatus.Archived)
        {
            ErrorMessage = $"Dự án \"{project.Name}\" đang ở trạng thái {project.Status} và chưa thể truy cập Board.";
            return;
        }

        ProjectId = project.Id;
        ProjectName = project.Name;
        CurrentUserId = userId;
        IsPm = membership.Role == WorkspaceMemberRole.PM;

        var sessionJson = HttpContext.Session.GetString(TaskFilterSession.KeyForProject(projectId));
        var criteria = TaskFilterSession.Deserialize(sessionJson);

        var members = await _taskService.GetWorkspaceMemberFilterOptionsAsync(project.WorkspaceId, cancellationToken);

        try
         {
             Board = await _taskService.GetFilteredKanbanTasksAsync(
                 projectId,
                 project.WorkspaceId,
                 userId,
                 criteria,
                 cancellationToken);
         }
         catch (Exception ex)
         {
             ErrorMessage = ex.Message;
         }

        Filters = new TaskFiltersVm
         {
             ProjectId = project.Id,
             WorkspaceId = project.WorkspaceId,
             ViewContext = "kanban",
             Criteria = criteria,
             WorkspaceMembers = members,
             IsPm = IsPm
         };
     }
 }
