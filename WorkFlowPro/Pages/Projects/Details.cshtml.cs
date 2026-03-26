using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;

namespace WorkFlowPro.Pages.Projects;

[Authorize]
public sealed class DetailsModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;

    public DetailsModel(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
    }

    public Project? Project { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool CanManage { get; private set; }
    public Guid? CurrentWorkspaceId => _currentWorkspaceService.CurrentWorkspaceId;

    public async Task OnGetAsync([FromRoute] Guid projectId, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Workspace không hợp lệ.";
            return;
        }

        var isMember = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId &&
            m.WorkspaceId == workspaceId.Value,
            cancellationToken);

        if (!isMember)
        {
            Forbid();
        }

        Project = await _db.Projects.FirstOrDefaultAsync(p =>
            p.Id == projectId &&
            p.WorkspaceId == workspaceId.Value,
            cancellationToken);

        if (Project is null)
        {
            ErrorMessage = "Project không tồn tại trong workspace hiện tại.";
            return;
        }

        CanManage = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId &&
            m.WorkspaceId == workspaceId.Value &&
            m.Role == WorkspaceMemberRole.PM,
            cancellationToken);
    }
}
