using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace WorkFlowPro.Auth;

public sealed class IsPmAuthorizationHandler : AuthorizationHandler<IsPmRequirement>
{
    private readonly WorkFlowPro.Data.WorkFlowProDbContext _db;
    private readonly ICurrentWorkspaceService _currentWorkspaceService;

    public IsPmAuthorizationHandler(
        WorkFlowPro.Data.WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspaceService)
    {
        _db = db;
        _currentWorkspaceService = currentWorkspaceService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        IsPmRequirement requirement)
    {
        if (context.User?.Identity is null || !context.User.Identity.IsAuthenticated)
            return;

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
            return;

        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.UserId == userId &&
            m.WorkspaceId == workspaceId.Value &&
            m.Role == WorkFlowPro.Data.WorkspaceMemberRole.PM);

        if (isPm)
            context.Succeed(requirement);
    }
}

