using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Tasks;

[Authorize]
public sealed class MyTasksPendingModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;

    public MyTasksPendingModel(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
    }

    public string? ErrorMessage { get; private set; }
    public List<PendingTaskVm> PendingTasks { get; private set; } = new();

    public sealed record PendingTaskVm(
        Guid TaskId,
        string Title,
        string ProjectName,
        TaskPriority Priority,
        DateTime? DueDateUtc);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Workspace không hợp lệ.";
            return;
        }

        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
        {
            ErrorMessage = "User không hợp lệ.";
            return;
        }

        // UC-05: Only members (not PM) should accept/reject. Here we show pending tasks
        // to members as entry point from notifications.
        var isPm = await _db.WorkspaceMembers.AnyAsync(
            m => m.WorkspaceId == workspaceId.Value && m.UserId == actorUserId && m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        if (isPm)
        {
            ErrorMessage = "Trang này chỉ dành cho Member (không phải PM).";
            return;
        }

        var pendingTasks = await (from a in _db.TaskAssignments
                                   where a.AssigneeUserId == actorUserId &&
                                         a.Status == TaskAssignmentStatus.Pending
                                   join t in _db.Tasks on a.TaskId equals t.Id
                                   join p in _db.Projects on t.ProjectId equals p.Id
                                   where p.WorkspaceId == workspaceId.Value
                                   select new PendingTaskVm(
                                       t.Id,
                                       t.Title,
                                       p.Name,
                                       t.Priority,
                                       t.DueDateUtc))
            .ToListAsync(cancellationToken);

        PendingTasks = pendingTasks;
    }
}

