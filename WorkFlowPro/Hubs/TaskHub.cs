using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Extensions;
using WorkFlowPro.Services;
using TaskStatus = WorkFlowPro.Data.TaskStatus;

namespace WorkFlowPro.Hubs;

[Authorize]
public sealed class TaskHub : Hub
{
    public static string WorkspaceGroupName(Guid workspaceId) => $"ws:{workspaceId:D}";

    private readonly IKanbanService _kanbanService;
    private readonly WorkFlowProDbContext _db;

    public TaskHub(IKanbanService kanbanService, WorkFlowProDbContext db)
    {
        _kanbanService = kanbanService;
        _db = db;
    }

    /// <summary>UC-14 / UC-04: nhận broadcast cập nhật MemberProfile (gợi ý phân công).</summary>
    public async Task JoinWorkspace(Guid workspaceId)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        var ok = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId && m.UserId == userId);
        if (!ok)
            return;

        await Groups.AddToGroupAsync(Context.ConnectionId, WorkspaceGroupName(workspaceId));
    }

    public async Task JoinProject(Guid projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, projectId.ToString("D"));
    }

    public async Task LeaveProject(Guid projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, projectId.ToString("D"));
    }

    // Called by Kanban board drag & drop.
    public async Task<MoveTaskHubResult> MoveTask(
        Guid taskId,
        string targetStatus,
        CancellationToken cancellationToken = default)
    {
        var principal = Context.User;
        if (principal is null)
            return new MoveTaskHubResult(false, "Not authenticated.");

        var actorUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return new MoveTaskHubResult(false, "Missing user id.");

        // workspace_id is injected by WorkspaceClaimsTransformation (UC-15).
        var workspaceIdStr = principal.FindFirstValue("workspace_id") ?? principal.FindFirstValue("CurrentWorkspaceId");
        if (!Guid.TryParse(workspaceIdStr, out var workspaceId))
            return new MoveTaskHubResult(false, "Missing workspace id.");

        if (!Enum.TryParse<TaskStatus>(targetStatus, ignoreCase: true, out var newStatus))
            return new MoveTaskHubResult(false, "Invalid target status.");

        var result = await _kanbanService.UpdateTaskStatusAsync(
            taskId,
            newStatus,
            actorUserId,
            workspaceId,
            cancellationToken);

        return new MoveTaskHubResult(result.Success, result.ErrorMessage);
    }
}

public sealed record MoveTaskHubResult(bool Success, string? ErrorMessage = null);

