using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WorkFlowPro.Hubs;

/// <summary>UC-11: Push thông báo realtime tới user (<see cref="IUserIdProvider" />).</summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    /// <summary>Prefix nhóm workspace — dùng cho broadcast theo workspace (tùy chọn, song song với <c>Clients.User</c>).</summary>
    public const string WorkspaceGroupPrefix = "ws:";

    public override async Task OnConnectedAsync()
    {
        var ws =
            Context.User?.FindFirstValue("CurrentWorkspaceId")
            ?? Context.User?.FindFirstValue("workspace_id");
        if (Guid.TryParse(ws, out var wid))
        {
            await Groups.AddToGroupAsync(
                Context.ConnectionId,
                WorkspaceGroupPrefix + wid.ToString("D"));
        }

        await base.OnConnectedAsync();
    }
}
