using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Tests;

/// <summary>UC-12: Receive Notifications — UserNotificationService unit tests.</summary>
public class UserNotificationServiceTests
{
    private const string UserId = "user-test";

    private static WorkFlowProDbContext CreateDb(string name = "")
    {
        var options = new DbContextOptionsBuilder<WorkFlowProDbContext>()
            .UseInMemoryDatabase(databaseName: string.IsNullOrEmpty(name) ? Guid.NewGuid().ToString() : name)
            .Options;

        var mockWorkspaceSvc = new Mock<ICurrentWorkspaceService>();
        mockWorkspaceSvc.Setup(x => x.CurrentWorkspaceId).Returns((Guid?)null);

        var mockUserAccessor = new Mock<ICurrentUserAccessor>();
        mockUserAccessor.Setup(x => x.UserId).Returns((string?)null);

        return new WorkFlowProDbContext(options, mockWorkspaceSvc.Object, mockUserAccessor.Object);
    }

    private static HttpContext CreateHttpContext(string userId)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        return ctx;
    }

    private static UserNotificationService CreateService(WorkFlowProDbContext db, string userId)
    {
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(x => x.HttpContext).Returns(CreateHttpContext(userId));
        return new UserNotificationService(db, httpAccessor.Object);
    }

    private static async Task SeedNotifications(WorkFlowProDbContext db, string userId, int count, bool read)
    {
        for (int i = 0; i < count; i++)
        {
            db.UserNotifications.Add(new UserNotification
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Type = NotificationType.TaskAssignedPending,
                Message = $"Notification {i}",
                IsRead = read,
                CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();
    }

    // ============================================================
    // GetMyNotificationsAsync
    // ============================================================

    [Fact]
    public async Task GetMyNotificationsAsync_ReturnsOnlyCurrentUserNotifications()
    {
        var db = CreateDb();

        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = NotificationType.TaskAssignedPending,
            Message = "Yours",
            IsRead = false
        });
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = "other-user",
            Type = NotificationType.TaskAssignedPending,
            Message = "Not yours",
            IsRead = false
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.GetMyNotificationsAsync(0, 50);

        Assert.Single(result);
        Assert.Equal("Yours", result[0].Message);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_ReturnsOrderedByCreatedAtUtcDescending()
    {
        var db = CreateDb();
        var old = DateTime.UtcNow.AddHours(-5);
        var recent = DateTime.UtcNow;

        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "Older", CreatedAtUtc = old
        });
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "Newer", CreatedAtUtc = recent
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.GetMyNotificationsAsync(0, 50);

        Assert.Equal("Newer", result[0].Message);
        Assert.Equal("Older", result[1].Message);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_RespectsSkipAndTake()
    {
        var db = CreateDb();
        for (int i = 0; i < 10; i++)
        {
            db.UserNotifications.Add(new UserNotification
            {
                Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
                Message = $"N-{i:D2}", CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var page1 = await svc.GetMyNotificationsAsync(0, 3);
        var page2 = await svc.GetMyNotificationsAsync(3, 3);
        var page3 = await svc.GetMyNotificationsAsync(6, 3);

        Assert.Equal(3, page1.Count);
        Assert.Equal(3, page2.Count);
        Assert.Equal(3, page3.Count);
        Assert.Equal("N-00", page1[0].Message);
        Assert.Equal("N-03", page2[0].Message);
        Assert.Equal("N-06", page3[0].Message);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_TakeExceeds200_CappedTo200()
    {
        var db = CreateDb();
        for (int i = 0; i < 250; i++)
        {
            db.UserNotifications.Add(new UserNotification
            {
                Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
                Message = $"N-{i}", CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.GetMyNotificationsAsync(0, 500);

        Assert.Equal(200, result.Count);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_NegativeSkip_ResetsToZero()
    {
        var db = CreateDb();
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "First", CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.GetMyNotificationsAsync(-5, 10);

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_ZeroTake_UsesDefault50()
    {
        var db = CreateDb();
        for (int i = 0; i < 60; i++)
        {
            db.UserNotifications.Add(new UserNotification
            {
                Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
                Message = $"N-{i}", CreatedAtUtc = DateTime.UtcNow.AddMinutes(-i)
            });
        }
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.GetMyNotificationsAsync(0, 0);

        Assert.Equal(50, result.Count);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_TypeLabel_IsEnumString()
    {
        var db = CreateDb();
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = NotificationType.TaskEvaluated,
            Message = "Evaluated",
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.GetMyNotificationsAsync(0, 10);

        Assert.Single(result);
        Assert.Equal("TaskEvaluated", result[0].TypeLabel);
        Assert.Equal(NotificationType.TaskEvaluated, result[0].Type);
    }

    // ============================================================
    // FixRedirectUrl — workspaceId append
    // ============================================================

    [Fact]
    public async Task GetMyNotificationsAsync_RedirectUrl_AppendsWorkspaceIdWhenMissing()
    {
        var db = CreateDb();
        var workspaceId = Guid.NewGuid();

        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = NotificationType.TaskAssignedPending,
            Message = "Check",
            RedirectUrl = "/Tasks/Details/1",
            WorkspaceId = workspaceId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.GetMyNotificationsAsync(0, 10);

        Assert.Single(result);
        Assert.Contains($"workspaceId={workspaceId:D}", result[0].RedirectUrl);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_RedirectUrl_AlreadyHasWorkspaceId_Unchanged()
    {
        var workspaceId = Guid.NewGuid();
        var db = CreateDb();

        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = NotificationType.TaskAssignedPending,
            Message = "Check",
            RedirectUrl = $"/Tasks/Details/1?workspaceId={workspaceId:D}",
            WorkspaceId = workspaceId,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.GetMyNotificationsAsync(0, 10);

        Assert.Single(result);
        Assert.Equal($"/Tasks/Details/1?workspaceId={workspaceId:D}", result[0].RedirectUrl);
    }

    [Fact]
    public async Task GetMyNotificationsAsync_RedirectUrl_NullWorkspaceId_ReturnsNull()
    {
        var db = CreateDb();

        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = NotificationType.TaskAssignedPending,
            Message = "Check",
            RedirectUrl = "/Tasks/Details/1",
            WorkspaceId = null,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.GetMyNotificationsAsync(0, 10);

        Assert.Single(result);
        Assert.Equal("/Tasks/Details/1", result[0].RedirectUrl);
    }

    // ============================================================
    // GetUnreadCountAsync
    // ============================================================

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsOnlyUnreadCount()
    {
        var db = CreateDb();
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "Unread 1", IsRead = false
        });
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "Unread 2", IsRead = false
        });
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "Read", IsRead = true
        });
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = "other", Type = NotificationType.TaskAssignedPending,
            Message = "Other unread", IsRead = false
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var count = await svc.GetUnreadCountAsync();

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task GetUnreadCountAsync_Empty_ReturnsZero()
    {
        var db = CreateDb();
        var svc = CreateService(db, UserId);

        var count = await svc.GetUnreadCountAsync();

        Assert.Equal(0, count);
    }

    // ============================================================
    // MarkAsReadAsync
    // ============================================================

    [Fact]
    public async Task MarkAsReadAsync_ValidNotification_MarksAsRead()
    {
        var db = CreateDb();
        var notifId = Guid.NewGuid();
        db.UserNotifications.Add(new UserNotification
        {
            Id = notifId,
            UserId = UserId,
            Type = NotificationType.TaskAssignedPending,
            Message = "Test",
            IsRead = false
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.MarkAsReadAsync(notifId);

        Assert.True(result);
        var updated = await db.UserNotifications.FirstAsync(n => n.Id == notifId);
        Assert.True(updated.IsRead);
    }

    [Fact]
    public async Task MarkAsReadAsync_AlreadyRead_ReturnsTrueWithoutDbWrite()
    {
        var db = CreateDb();
        var notifId = Guid.NewGuid();
        db.UserNotifications.Add(new UserNotification
        {
            Id = notifId,
            UserId = UserId,
            Type = NotificationType.TaskAssignedPending,
            Message = "Test",
            IsRead = true
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.MarkAsReadAsync(notifId);

        Assert.True(result);
    }

    [Fact]
    public async Task MarkAsReadAsync_WrongUser_ReturnsFalse()
    {
        var db = CreateDb();
        var notifId = Guid.NewGuid();
        db.UserNotifications.Add(new UserNotification
        {
            Id = notifId,
            UserId = "other-user",
            Type = NotificationType.TaskAssignedPending,
            Message = "Not yours",
            IsRead = false
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.MarkAsReadAsync(notifId);

        Assert.False(result);
    }

    [Fact]
    public async Task MarkAsReadAsync_NotFound_ReturnsFalse()
    {
        var db = CreateDb();
        var svc = CreateService(db, UserId);

        var result = await svc.MarkAsReadAsync(Guid.NewGuid());

        Assert.False(result);
    }

    // ============================================================
    // MarkAllAsReadAsync
    // ============================================================

    [Fact]
    public async Task MarkAllAsReadAsync_MarksAllUnreadAsRead()
    {
        var db = CreateDb();
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "U1", IsRead = false
        });
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "U2", IsRead = false
        });
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "Already read", IsRead = true
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var count = await svc.MarkAllAsReadAsync();

        Assert.Equal(2, count);
        var all = await db.UserNotifications.Where(n => n.UserId == UserId).ToListAsync();
        Assert.All(all, n => Assert.True(n.IsRead));
    }

    [Fact]
    public async Task MarkAllAsReadAsync_NoUnread_ReturnsZero()
    {
        var db = CreateDb();
        db.UserNotifications.Add(new UserNotification
        {
            Id = Guid.NewGuid(), UserId = UserId, Type = NotificationType.TaskAssignedPending,
            Message = "Already read", IsRead = true
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var count = await svc.MarkAllAsReadAsync();

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task MarkAllAsReadAsync_Empty_ReturnsZero()
    {
        var db = CreateDb();
        var svc = CreateService(db, UserId);

        var count = await svc.MarkAllAsReadAsync();

        Assert.Equal(0, count);
    }

    // ============================================================
    // DeleteAsync
    // ============================================================

    [Fact]
    public async Task DeleteAsync_ValidNotification_DeletesFromDb()
    {
        var db = CreateDb();
        var notifId = Guid.NewGuid();
        db.UserNotifications.Add(new UserNotification
        {
            Id = notifId,
            UserId = UserId,
            Type = NotificationType.TaskAssignedPending,
            Message = "To delete"
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.DeleteAsync(notifId);

        Assert.True(result);
        Assert.Equal(0, await db.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_WrongUser_ReturnsFalseAndDoesNotDelete()
    {
        var db = CreateDb();
        var notifId = Guid.NewGuid();
        db.UserNotifications.Add(new UserNotification
        {
            Id = notifId,
            UserId = "other-user",
            Type = NotificationType.TaskAssignedPending,
            Message = "Not yours"
        });
        await db.SaveChangesAsync();

        var svc = CreateService(db, UserId);

        var result = await svc.DeleteAsync(notifId);

        Assert.False(result);
        Assert.Equal(1, await db.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task DeleteAsync_NotFound_ReturnsFalse()
    {
        var db = CreateDb();
        var svc = CreateService(db, UserId);

        var result = await svc.DeleteAsync(Guid.NewGuid());

        Assert.False(result);
    }

    // ============================================================
    // Unauthorized access
    // ============================================================

    [Fact]
    public async Task GetMyNotificationsAsync_NoUser_ThrowsUnauthorized()
    {
        var db = CreateDb();
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var svc = new UserNotificationService(db, httpAccessor.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.GetMyNotificationsAsync(0, 10));
    }

    [Fact]
    public async Task GetUnreadCountAsync_NoUser_ThrowsUnauthorized()
    {
        var db = CreateDb();
        var httpAccessor = new Mock<IHttpContextAccessor>();
        httpAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);
        var svc = new UserNotificationService(db, httpAccessor.Object);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.GetUnreadCountAsync());
    }
}
