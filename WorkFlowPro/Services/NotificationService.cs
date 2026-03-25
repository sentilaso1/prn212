using Microsoft.AspNetCore.SignalR;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;

namespace WorkFlowPro.Services;

public interface INotificationService
{
    Task<Guid> CreateAndPushAsync(
        string userId,
        NotificationType type,
        string message,
        Guid? workspaceId = null,
        Guid? projectId = null,
        Guid? taskId = null,
        string? redirectUrl = null,
        CancellationToken cancellationToken = default);
}

public sealed class NotificationService : INotificationService
{
    private readonly WorkFlowProDbContext _db;
    private readonly IHubContext<KanbanHub> _hub;

    public NotificationService(WorkFlowProDbContext db, IHubContext<KanbanHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<Guid> CreateAndPushAsync(
        string userId,
        NotificationType type,
        string message,
        Guid? workspaceId = null,
        Guid? projectId = null,
        Guid? taskId = null,
        string? redirectUrl = null,
        CancellationToken cancellationToken = default)
    {
        var notif = new UserNotification
        {
            UserId = userId,
            WorkspaceId = workspaceId,
            ProjectId = projectId,
            TaskId = taskId,
            Type = type,
            Message = message,
            IsRead = false,
            RedirectUrl = redirectUrl,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.UserNotifications.Add(notif);
        await _db.SaveChangesAsync(cancellationToken);

        // UC-11: realtime notification via SignalR
        await _hub.Clients.User(userId).SendAsync(
            "notification",
            new
            {
                notificationId = notif.Id,
                type = notif.Type.ToString(),
                message = notif.Message,
                redirectUrl = notif.RedirectUrl
            },
            cancellationToken);

        return notif.Id;
    }
}

