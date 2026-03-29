using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;

namespace WorkFlowPro.Auth;

public sealed class PlatformAdminHandler : AuthorizationHandler<PlatformAdminRequirement>
{
    private readonly WorkFlowProDbContext _db;

    public PlatformAdminHandler(WorkFlowProDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PlatformAdminRequirement requirement)
    {
        if (context.User.Identity is null || !context.User.Identity.IsAuthenticated)
            return;

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        if (context.User.HasClaim("platform_role", "admin"))
        {
            context.Succeed(requirement);
            return;
        }

        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsPlatformAdmin))
            context.Succeed(requirement);
    }
}
