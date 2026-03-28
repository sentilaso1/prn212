using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;

namespace WorkFlowPro.Pages.Projects;

/// <summary>Thành viên workspace (dự án dùng chung đơn vị — không có bảng ProjectMember).</summary>
public sealed record ProjectWorkspaceMemberRowVm(
    string UserId,
    string DisplayName,
    string? Email,
    WorkspaceMemberRole Role,
    string? SubRole);

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

    /// <summary>Để build link Kanban / danh sách task / tạo task.</summary>
    public Guid? CurrentWorkspaceId { get; private set; }

    public IReadOnlyList<ProjectWorkspaceMemberRowVm> WorkspaceMembers { get; private set; } =
        Array.Empty<ProjectWorkspaceMemberRowVm>();

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

        CurrentWorkspaceId = workspaceId.Value;

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

        WorkspaceMembers = await (
            from m in _db.WorkspaceMembers
            where m.WorkspaceId == workspaceId.Value
            join u in _db.Users on m.UserId equals u.Id
            orderby m.Role, u.DisplayName, u.Email
            select new ProjectWorkspaceMemberRowVm(
                m.UserId,
                u.DisplayName ?? u.Email ?? u.UserName ?? m.UserId,
                u.Email,
                m.Role,
                m.SubRole)
        ).ToListAsync(cancellationToken);
    }
}
