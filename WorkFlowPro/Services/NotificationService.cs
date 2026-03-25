using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;

namespace WorkFlowPro.Services;

public interface INotificationService
{
    /// <returns>Id thông báo, hoặc <see cref="Guid.Empty" /> nếu bỏ qua (validation).</returns>
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
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        WorkFlowProDbContext db,
        IHubContext<NotificationHub> hub,
        ILogger<NotificationService> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
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
        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Skip notification: empty userId.");
            return Guid.Empty;
        }

        if (workspaceId.HasValue)
        {
            var isMember = await _db.WorkspaceMembers.AnyAsync(
                m => m.WorkspaceId == workspaceId.Value && m.UserId == userId,
                cancellationToken);
            if (!isMember)
            {
                _logger.LogWarning(
                    "Skip notification: user {UserId} không thuộc workspace {WorkspaceId}.",
                    userId,
                    workspaceId);
                return Guid.Empty;
            }
        }

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

        try
        {
            await _hub.Clients.User(userId).SendAsync(
                "notification",
                new
                {
                    notificationId = notif.Id,
                    type = notif.Type.ToString(),
                    message = notif.Message,
                    redirectUrl = notif.RedirectUrl,
                    isRead = false,
                    createdAtUtc = notif.CreatedAtUtc
                },
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SignalR push notification failed for {UserId}", userId);
        }

        return notif.Id;
    }
}
