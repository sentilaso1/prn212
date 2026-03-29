using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Tasks;

/// <summary>Trung tâm vào UC task: chọn dự án rồi mở /tasks/list (cần projectId).</summary>
[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspace;

    public IndexModel(WorkFlowProDbContext db, ICurrentWorkspaceService currentWorkspace)
    {
        _db = db;
        _currentWorkspace = currentWorkspace;
    }

    public string? ErrorMessage { get; private set; }

    public Guid? WorkspaceId { get; private set; }

    public IReadOnlyList<ProjectRowVm> Projects { get; private set; } = Array.Empty<ProjectRowVm>();

    public sealed record ProjectRowVm(Guid Id, string Name);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Chưa có đơn vị hiện tại. Vào Đơn vị hoặc chọn đơn vị trên thanh menu trước.";
            return;
        }

        WorkspaceId = workspaceId;

        var isMember = await _db.WorkspaceMembers.AnyAsync(
            m => m.WorkspaceId == workspaceId.Value && m.UserId == userId,
            cancellationToken);
        if (!isMember)
        {
            ErrorMessage = "Bạn không thuộc đơn vị hiện tại.";
            return;
        }

        var projects = await _db.Projects.AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId.Value)
            .OrderBy(p => p.Name)
            .Select(p => new ProjectRowVm(p.Id, p.Name))
            .ToListAsync(cancellationToken);

        Projects = projects;
    }
}
