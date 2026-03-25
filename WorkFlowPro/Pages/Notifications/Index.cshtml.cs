using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Notifications;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly IUserNotificationService _notifications;

    public IndexModel(IUserNotificationService notifications)
    {
        _notifications = notifications;
    }

    public IReadOnlyList<UserNotificationItemVm> Items { get; private set; } = Array.Empty<UserNotificationItemVm>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Items = await _notifications.GetMyNotificationsAsync(0, 200, cancellationToken);
    }
}
