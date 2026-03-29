using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;

namespace WorkFlowPro.Pages.Invite;

[Authorize(Policy = "IsPM")]
public sealed class SentModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;

    public SentModel(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
    }

    public sealed class SentInviteVm
    {
        public string Email { get; set; } = default!;
        public WorkspaceMemberRole Role { get; set; }
        public string? SubRole { get; set; }
        public InviteStatus Status { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    public IReadOnlyList<SentInviteVm> Invites { get; private set; } = Array.Empty<SentInviteVm>();
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Workspace khong hop le.";
            return;
        }

        var raw = await _db.WorkspaceInviteTokens
            .AsNoTracking()
            .Where(t => t.WorkspaceId == workspaceId.Value)
            .OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new
            {
                t.Email,
                t.Role,
                t.SubRole,
                t.Status,
                t.CreatedAtUtc,
                t.ExpiresAtUtc
            })
            .ToListAsync(cancellationToken);

        Invites = raw
            .Select(t => new SentInviteVm
            {
                Email = t.Email,
                Role = t.Role,
                SubRole = t.SubRole,
                Status = t.Status,
                CreatedAtUtc = t.CreatedAtUtc,
                ExpiresAtUtc = t.ExpiresAtUtc
            })
            .ToList();
    }
}
