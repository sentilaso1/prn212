using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
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
    private readonly ICurrentWorkspaceService _currentWorkspace;

    public TaskHub(
        IKanbanService kanbanService,
        WorkFlowProDbContext db,
        ICurrentWorkspaceService currentWorkspace)
    {
        _kanbanService = kanbanService;
        _db = db;
        _currentWorkspace = currentWorkspace;
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

    /// <summary>
    /// Kanban kéo thả. Tham số string + Dictionary trả về — tránh lỗi bind Guid / serialize record trên SignalR.
    /// Load session trước khi đọc workspace (WebSocket thường cần LoadAsync).
    /// </summary>
    public async Task<Dictionary<string, object?>> MoveTask(string taskId, string targetStatus)
    {
        static Dictionary<string, object?> R(bool success, string? errorMessage = null) =>
            new()
            {
                ["success"] = success,
                ["errorMessage"] = errorMessage
            };

        try
        {
            var http = Context.GetHttpContext();
            if (http?.Session is { } session)
            {
                try
                {
                    await session.LoadAsync(Context.ConnectionAborted);
                }
                catch
                {
                    /* một số cấu hình session không cần / không hỗ trợ Load trên WS */
                }
            }

            var principal = Context.User;
            if (principal is null)
                return R(false, "Not authenticated.");

            var actorUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrWhiteSpace(actorUserId))
                return R(false, "Missing user id.");

            if (!Guid.TryParse(taskId?.Trim(), out var tid) || tid == Guid.Empty)
                return R(false, "taskId không hợp lệ.");

            Guid workspaceGuid;
            var wsFromSession = _currentWorkspace.CurrentWorkspaceId;
            if (wsFromSession is { } w && w != Guid.Empty)
                workspaceGuid = w;
            else if (!Guid.TryParse(
                         principal.FindFirstValue("workspace_id") ?? principal.FindFirstValue("CurrentWorkspaceId"),
                         out workspaceGuid))
                return R(false, "Thiếu workspace hiện tại — hãy chọn lại đơn vị hoặc tải lại trang.");

            if (string.IsNullOrWhiteSpace(targetStatus) ||
                !Enum.TryParse<TaskStatus>(targetStatus.Trim(), ignoreCase: true, out var newStatus))
                return R(false, "Trạng thái đích không hợp lệ.");

            var result = await _kanbanService.UpdateTaskStatusAsync(
                tid,
                newStatus,
                actorUserId,
                workspaceGuid,
                Context.ConnectionAborted);

            return R(result.Success, result.ErrorMessage);
        }
        catch (Exception ex)
        {
            return R(false, $"Lỗi server khi chuyển task: {ex.Message}");
        }
    }
}
