using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using WorkFlowPro.Controllers;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Tests;

/// <summary>UC-12: Receive Notifications — NotificationsController API unit tests.</summary>
public class NotificationsControllerTests
{
    private static NotificationsController CreateController(Mock<IUserNotificationService> mockService)
    {
        var ctrl = new NotificationsController(mockService.Object);

        var user = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                new[] { new System.Security.Claims.Claim(
                    System.Security.Claims.ClaimTypes.NameIdentifier,
                    "user-controller-test") },
                "TestAuth"));

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        return ctrl;
    }

    // ============================================================
    // GET /api/notifications — List
    // ============================================================

    [Fact]
    public async Task List_ReturnsOkWithNotifications()
    {
        var mockService = new Mock<IUserNotificationService>();
        var notifications = new List<UserNotificationItemVm>
        {
            new(Guid.NewGuid(), NotificationType.TaskAssignedPending,
                "TaskAssignedPending", "Task assigned", false, "/tasks/1", DateTime.UtcNow),
            new(Guid.NewGuid(), NotificationType.TaskEvaluated,
                "TaskEvaluated", "Task evaluated", true, null, DateTime.UtcNow)
        };
        mockService.Setup(x => x.GetMyNotificationsAsync(0, 30, default))
            .ReturnsAsync(notifications);

        var ctrl = CreateController(mockService);

        var result = await ctrl.List();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var returned = Assert.IsAssignableFrom<IReadOnlyList<UserNotificationItemVm>>(ok.Value);
        Assert.Equal(2, returned.Count);
    }

    [Fact]
    public async Task List_RespectsSkipAndTakeParameters()
    {
        var mockService = new Mock<IUserNotificationService>();
        mockService.Setup(x => x.GetMyNotificationsAsync(10, 50, default))
            .ReturnsAsync(new List<UserNotificationItemVm>());

        var ctrl = CreateController(mockService);

        await ctrl.List(skip: 10, take: 50);

        mockService.Verify(x => x.GetMyNotificationsAsync(10, 50, default), Times.Once);
    }

    [Fact]
    public async Task List_DefaultsToSkip0Take30()
    {
        var mockService = new Mock<IUserNotificationService>();
        mockService.Setup(x => x.GetMyNotificationsAsync(0, 30, default))
            .ReturnsAsync(new List<UserNotificationItemVm>());

        var ctrl = CreateController(mockService);

        await ctrl.List();

        mockService.Verify(x => x.GetMyNotificationsAsync(0, 30, default), Times.Once);
    }

    [Fact]
    public async Task List_Empty_ReturnsOkWithEmptyList()
    {
        var mockService = new Mock<IUserNotificationService>();
        mockService.Setup(x => x.GetMyNotificationsAsync(It.IsAny<int>(), It.IsAny<int>(), default))
            .ReturnsAsync(new List<UserNotificationItemVm>());

        var ctrl = CreateController(mockService);

        var result = await ctrl.List();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var returned = Assert.IsAssignableFrom<IReadOnlyList<UserNotificationItemVm>>(ok.Value);
        Assert.Empty(returned);
    }

    // ============================================================
    // GET /api/notifications/unread-count
    // ============================================================

    [Fact]
    public async Task UnreadCount_ReturnsOkWithCount()
    {
        var mockService = new Mock<IUserNotificationService>();
        mockService.Setup(x => x.GetUnreadCountAsync(default))
            .ReturnsAsync(5);

        var ctrl = CreateController(mockService);

        var result = await ctrl.UnreadCount();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(5, ok.Value);
    }

    [Fact]
    public async Task UnreadCount_Zero_ReturnsOkZero()
    {
        var mockService = new Mock<IUserNotificationService>();
        mockService.Setup(x => x.GetUnreadCountAsync(default))
            .ReturnsAsync(0);

        var ctrl = CreateController(mockService);

        var result = await ctrl.UnreadCount();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(0, ok.Value);
    }

    // ============================================================
    // POST /api/notifications/{id}/read — Mark as read
    // ============================================================

    [Fact]
    public async Task MarkRead_ExistingNotification_ReturnsOk()
    {
        var mockService = new Mock<IUserNotificationService>();
        var notifId = Guid.NewGuid();
        mockService.Setup(x => x.MarkAsReadAsync(notifId, default))
            .ReturnsAsync(true);

        var ctrl = CreateController(mockService);

        var result = await ctrl.MarkRead(notifId);

        Assert.IsType<OkResult>(result);
        mockService.Verify(x => x.MarkAsReadAsync(notifId, default), Times.Once);
    }

    [Fact]
    public async Task MarkRead_NonExistingNotification_ReturnsNotFound()
    {
        var mockService = new Mock<IUserNotificationService>();
        var notifId = Guid.NewGuid();
        mockService.Setup(x => x.MarkAsReadAsync(notifId, default))
            .ReturnsAsync(false);

        var ctrl = CreateController(mockService);

        var result = await ctrl.MarkRead(notifId);

        Assert.IsType<NotFoundResult>(result);
    }

    // ============================================================
    // POST /api/notifications/read-all
    // ============================================================

    [Fact]
    public async Task MarkAllRead_ReturnsCountOfMarkedNotifications()
    {
        var mockService = new Mock<IUserNotificationService>();
        mockService.Setup(x => x.MarkAllAsReadAsync(default))
            .ReturnsAsync(7);

        var ctrl = CreateController(mockService);

        var result = await ctrl.MarkAllRead();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(7, ok.Value);
    }

    [Fact]
    public async Task MarkAllRead_NoUnread_ReturnsZero()
    {
        var mockService = new Mock<IUserNotificationService>();
        mockService.Setup(x => x.MarkAllAsReadAsync(default))
            .ReturnsAsync(0);

        var ctrl = CreateController(mockService);

        var result = await ctrl.MarkAllRead();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(0, ok.Value);
    }

    // ============================================================
    // DELETE /api/notifications/{id}
    // ============================================================

    [Fact]
    public async Task Delete_ExistingNotification_ReturnsNoContent()
    {
        var mockService = new Mock<IUserNotificationService>();
        var notifId = Guid.NewGuid();
        mockService.Setup(x => x.DeleteAsync(notifId, default))
            .ReturnsAsync(true);

        var ctrl = CreateController(mockService);

        var result = await ctrl.Delete(notifId);

        Assert.IsType<NoContentResult>(result);
        mockService.Verify(x => x.DeleteAsync(notifId, default), Times.Once);
    }

    [Fact]
    public async Task Delete_NonExistingNotification_ReturnsNotFound()
    {
        var mockService = new Mock<IUserNotificationService>();
        var notifId = Guid.NewGuid();
        mockService.Setup(x => x.DeleteAsync(notifId, default))
            .ReturnsAsync(false);

        var ctrl = CreateController(mockService);

        var result = await ctrl.Delete(notifId);

        Assert.IsType<NotFoundResult>(result);
    }
}
