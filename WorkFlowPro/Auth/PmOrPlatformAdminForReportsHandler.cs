using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;

namespace WorkFlowPro.Auth;

public sealed class PmOrPlatformAdminForReportsHandler
    : AuthorizationHandler<PmOrPlatformAdminForReportsRequirement>
{
    private readonly WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspace;

    public PmOrPlatformAdminForReportsHandler(
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspace)
    {
        _db = db;
        _currentWorkspace = currentWorkspace;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PmOrPlatformAdminForReportsRequirement requirement)
    {
        if (context.User.Identity is null || !context.User.Identity.IsAuthenticated)
            return;

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return;

        var workspaceExists =
            await _db.Workspaces.AsNoTracking().AnyAsync(w => w.Id == workspaceId.Value);

        if (!workspaceExists)
            return;

        var isPlatformAdmin = context.User.HasClaim("platform_role", "admin")
            || await _db.Users.AsNoTracking()
                .AnyAsync(u => u.Id == userId && u.IsPlatformAdmin);

        if (isPlatformAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId &&
            m.WorkspaceId == workspaceId.Value &&
            m.Role == WorkspaceMemberRole.PM);

        if (isPm)
            context.Succeed(requirement);
    }
}
