using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Extensions;
using WorkFlowPro.Services;

namespace WorkFlowPro.Controllers;

[ApiController]
[Authorize(Policy = "PlatformAdmin")]
[Route("api/admin/workspaces")]
public sealed class AdminWorkspacesController : ControllerBase
{
    private readonly WorkFlowProDbContext _db;
    private readonly INotificationService _notifications;

    public AdminWorkspacesController(WorkFlowProDbContext db, INotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    public sealed record ChangeRoleRequest(WorkspaceMemberRole NewRole);

    [HttpPost("{workspaceId:guid}/members/{targetUserId}/role")]
    public async Task<ActionResult> ChangeRole(Guid workspaceId, string targetUserId, [FromBody] ChangeRoleRequest request)
    {
        var adminUserId = User.GetUserId();

        var member = await _db.WorkspaceMembers.FirstOrDefaultAsync(m =>
            m.WorkspaceId == workspaceId && m.UserId == targetUserId);
        if (member is null) return NotFound();

        var oldRole = member.Role;
        if (oldRole == request.NewRole) return Ok();

        // RB-06: workspace must have at least one PM
        if (oldRole == WorkspaceMemberRole.PM && request.NewRole == WorkspaceMemberRole.Member)
        {
            var pmCount = await _db.WorkspaceMembers.CountAsync(m =>
                m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM);

            if (pmCount <= 1)
                return BadRequest("Workspace must keep at least one PM.");
        }

        member.Role = request.NewRole;
        _db.WorkspaceMembers.Update(member);

        _db.RoleChangeLogs.Add(new RoleChangeLog
        {
            WorkspaceId = workspaceId,
            ChangedByUserId = adminUserId,
            TargetUserId = targetUserId,
            OldRole = oldRole,
            NewRole = request.NewRole,
            TimestampUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(HttpContext.RequestAborted);

        await _notifications.CreateAndPushAsync(
            userId: targetUserId,
            type: NotificationType.RoleChanged,
            message: $"Role trong workspace đã đổi: {oldRole} -> {request.NewRole}",
            workspaceId: workspaceId,
            cancellationToken: HttpContext.RequestAborted);

        return Ok();
    }
}

