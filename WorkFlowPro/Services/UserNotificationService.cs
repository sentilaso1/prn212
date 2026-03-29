using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public interface IUserNotificationService
{
    Task<IReadOnlyList<UserNotificationItemVm>> GetMyNotificationsAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default);

    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    Task<bool> MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);

    Task<int> MarkAllAsReadAsync(CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid notificationId, CancellationToken cancellationToken = default);
}

public sealed record UserNotificationItemVm(
    Guid Id,
    NotificationType Type,
    string TypeLabel,
    string Message,
    bool IsRead,
    string? RedirectUrl,
    DateTime CreatedAtUtc);

public sealed class UserNotificationService : IUserNotificationService
{
    private readonly WorkFlowProDbContext _db;

    public UserNotificationService(WorkFlowProDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<UserNotificationItemVm>> GetMyNotificationsAsync(
        int skip,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (take <= 0)
            take = 50;
        if (take > 200)
            take = 200;
        if (skip < 0)
            skip = 0;

        var raw = await _db.UserNotifications
            .AsNoTracking()
            .OrderByDescending(n => n.CreatedAtUtc)
            .Skip(skip)
            .Take(take)
            .Select(n => new
            {
                n.Id,
                n.Type,
                n.Message,
                n.IsRead,
                n.RedirectUrl,
                n.WorkspaceId,
                n.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        var list = raw
            .Select(n => new UserNotificationItemVm(
                n.Id,
                n.Type,
                n.Type.ToString(),
                n.Message,
                n.IsRead,
                FixRedirectUrl(n.RedirectUrl, n.WorkspaceId),
                n.CreatedAtUtc))
            .ToList();

        return list;
    }

    private static string? FixRedirectUrl(string? redirectUrl, Guid? workspaceId)
    {
        if (string.IsNullOrWhiteSpace(redirectUrl))
            return redirectUrl;

        if (workspaceId is null)
            return redirectUrl;

        // Bổ sung workspaceId để transformation chọn đúng đơn vị khi mở từ thông báo.
        if (!redirectUrl.StartsWith("/Projects/Details/", StringComparison.OrdinalIgnoreCase) &&
            !redirectUrl.StartsWith("/Tasks/AcceptReject/", StringComparison.OrdinalIgnoreCase))
            return redirectUrl;

        // Nếu đã có workspaceId thì không đụng thêm.
        if (redirectUrl.Contains("workspaceId=", StringComparison.OrdinalIgnoreCase))
            return redirectUrl;

        var joinChar = redirectUrl.Contains('?') ? "&" : "?";
        return $"{redirectUrl}{joinChar}workspaceId={workspaceId.Value:D}";
    }

    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        return await _db.UserNotifications
            .AsNoTracking()
            .CountAsync(n => !n.IsRead, cancellationToken);
    }

    public async Task<bool> MarkAsReadAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var n = await _db.UserNotifications.FirstOrDefaultAsync(
            x => x.Id == notificationId,
            cancellationToken);
        if (n is null)
            return false;

        if (n.IsRead)
            return true;

        n.IsRead = true;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        var rows = await _db.UserNotifications
            .Where(n => !n.IsRead)
            .ToListAsync(cancellationToken);

        foreach (var n in rows)
            n.IsRead = true;

        if (rows.Count == 0)
            return 0;

        await _db.SaveChangesAsync(cancellationToken);
        return rows.Count;
    }

    public async Task<bool> DeleteAsync(
        Guid notificationId,
        CancellationToken cancellationToken = default)
    {
        var n = await _db.UserNotifications.FirstOrDefaultAsync(
            x => x.Id == notificationId,
            cancellationToken);
        if (n is null)
            return false;

        _db.UserNotifications.Remove(n);
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
