using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkFlowPro.Extensions;
using WorkFlowPro.Services;

namespace WorkFlowPro.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly IUserNotificationService _notifications;

    public NotificationsController(IUserNotificationService notifications)
    {
        _notifications = notifications;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserNotificationItemVm>>> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 30,
        CancellationToken cancellationToken = default)
    {
        _ = User.GetUserId();
        var list = await _notifications.GetMyNotificationsAsync(skip, take, cancellationToken);
        return Ok(list);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<int>> UnreadCount(CancellationToken cancellationToken = default)
    {
        _ = User.GetUserId();
        var n = await _notifications.GetUnreadCountAsync(cancellationToken);
        return Ok(n);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult> MarkRead(Guid id, CancellationToken cancellationToken = default)
    {
        _ = User.GetUserId();
        var ok = await _notifications.MarkAsReadAsync(id, cancellationToken);
        if (!ok)
            return NotFound();
        return Ok();
    }

    [HttpPost("read-all")]
    public async Task<ActionResult<int>> MarkAllRead(CancellationToken cancellationToken = default)
    {
        _ = User.GetUserId();
        var n = await _notifications.MarkAllAsReadAsync(cancellationToken);
        return Ok(n);
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
    {
        _ = User.GetUserId();
        var ok = await _notifications.DeleteAsync(id, cancellationToken);
        if (!ok)
            return NotFound();
        return NoContent();
    }
}
