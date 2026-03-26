using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Projects;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly IProjectService _projectService;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly WorkFlowProDbContext _db;

    public IndexModel(
        IProjectService projectService,
        ICurrentWorkspaceService currentWorkspaceService,
        WorkFlowProDbContext db)
    {
        _projectService = projectService;
        _currentWorkspaceService = currentWorkspaceService;
        _db = db;
    }

    public IReadOnlyList<Project> Projects { get; private set; } = Array.Empty<Project>();
    public string? ErrorMessage { get; private set; }
    public bool IsPm { get; private set; }
    public Guid? CurrentWorkspaceId => _currentWorkspaceService.CurrentWorkspaceId;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ErrorMessage = "Not authenticated.";
            return;
        }

        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Workspace không hợp lệ.";
            return;
        }

        IsPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId &&
            m.WorkspaceId == workspaceId.Value &&
            m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        try
        {
            Projects = await _projectService.ListForWorkspaceAsync(userId, cancellationToken);
        }
        catch (UnauthorizedAccessException)
        {
            ErrorMessage = "Bạn không có quyền xem workspace này.";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
