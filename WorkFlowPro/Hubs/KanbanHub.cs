using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace WorkFlowPro.Hubs;

[Authorize]
public sealed class KanbanHub : Hub
{
    // Group per projectId so board subscribers get realtime updates.
    public async Task JoinProject(Guid projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, projectId.ToString("D"));
    }

    public async Task LeaveProject(Guid projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, projectId.ToString("D"));
    }
}

