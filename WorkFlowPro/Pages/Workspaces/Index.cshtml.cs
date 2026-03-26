using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;

namespace WorkFlowPro.Pages.Workspaces;

[Authorize]
public class IndexModel : PageModel
{
    private readonly WorkFlowProDbContext _db;

    public IndexModel(WorkFlowProDbContext db)
    {
        _db = db;
    }

    public sealed record WorkspaceVm(Guid Id, string Name, WorkspaceMemberRole Role);

    public IReadOnlyList<WorkspaceVm> Workspaces { get; private set; } = Array.Empty<WorkspaceVm>();

    public Guid? ActiveWorkspaceId { get; private set; }

    /// <summary>Vai trò của user trong đơn vị đang chọn (để hiện shortcut UC).</summary>
    public WorkspaceMemberRole? ActiveWorkspaceRole { get; private set; }

    public string? ErrorMessage { get; private set; }

    public string? InfoMessage { get; private set; }

    public async Task OnGetAsync(Guid? workspaceId)
    {
        InfoMessage = TempData["RegisterBlockedMessage"] as string;

        ErrorMessage = TempData["WorkspaceSwitchError"] as string;
        if (ErrorMessage is null)
        {
            ErrorMessage = HttpContext.Session.GetString(WorkspaceSessionKeys.WorkspaceSwitchError);
            if (ErrorMessage is not null)
                HttpContext.Session.Remove(WorkspaceSessionKeys.WorkspaceSwitchError);
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return;

        var raw = await _db.WorkspaceMembers
            .Where(m => m.UserId == userId)
            .Join(
                _db.Workspaces,
                m => m.WorkspaceId,
                w => w.Id,
                (m, w) => new { w.Id, w.Name, m.Role })
            .OrderBy(x => x.Name)
            .ToListAsync(HttpContext.RequestAborted);

        var list = raw.Select(x => new WorkspaceVm(x.Id, x.Name, x.Role)).ToList();
        Workspaces = list;

        var claimWorkspaceId =
            User.FindFirstValue("CurrentWorkspaceId")
            ?? User.FindFirstValue("workspace_id");

        Guid? claimWorkspaceIdParsed = null;
        if (!string.IsNullOrWhiteSpace(claimWorkspaceId) &&
            Guid.TryParse(claimWorkspaceId, out var parsed))
        {
            claimWorkspaceIdParsed = parsed;
        }

        ActiveWorkspaceId = workspaceId
            ?? claimWorkspaceIdParsed
            ?? (list.Count > 0 ? list[0].Id : (Guid?)null);

        if (ActiveWorkspaceId is { } aid)
            ActiveWorkspaceRole = list.FirstOrDefault(x => x.Id == aid)?.Role;
    }
}

