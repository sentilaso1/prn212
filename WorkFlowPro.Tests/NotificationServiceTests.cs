using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;
using WorkFlowPro.Services;

namespace WorkFlowPro.Tests;

/// <summary>UC-12: Receive Notifications — NotificationService unit tests.</summary>
public class NotificationServiceTests
{
    private static WorkFlowProDbContext CreateDb(string name = "")
    {
        var options = new DbContextOptionsBuilder<WorkFlowProDbContext>()
            .UseInMemoryDatabase(databaseName: string.IsNullOrEmpty(name) ? Guid.NewGuid().ToString() : name)
            .Options;

        var mockWorkspaceSvc = new Mock<WorkFlowPro.Auth.ICurrentWorkspaceService>();
        mockWorkspaceSvc.Setup(x => x.CurrentWorkspaceId).Returns((Guid?)null);

        var mockUserAccessor = new Mock<WorkFlowPro.Auth.ICurrentUserAccessor>();
        mockUserAccessor.Setup(x => x.UserId).Returns((string?)null);

        return new WorkFlowProDbContext(options, mockWorkspaceSvc.Object, mockUserAccessor.Object);
    }

    private static Mock<IHubContext<NotificationHub>> CreateMockHubContext()
    {
        var mockClients = new Mock<IHubClients>();
        var mockClientProxy = new Mock<IClientProxy>();
        mockClients.Setup(c => c.User(It.IsAny<string>())).Returns(mockClientProxy.Object);
        var mockHubContext = new Mock<IHubContext<NotificationHub>>();
        mockHubContext.Setup(x => x.Clients).Returns(mockClients.Object);
        return mockHubContext;
    }

    // ============================================================
    // CreateAndPushAsync — Main sequence
    // ============================================================

    [Fact]
    public async Task CreateAndPushAsync_ValidUser_SavesNotificationAndPushesViaSignalR()
    {
        // Arrange
        var db = CreateDb();
        var userId = "user-1";
        db.Users.Add(new ApplicationUser { Id = userId, UserName = "u1@test.com", Email = "u1@test.com" });
        await db.SaveChangesAsync();

        var mockHub = CreateMockHubContext();
        var mockLogger = new Mock<ILogger<NotificationService>>();
        var svc = new NotificationService(db, mockHub.Object, mockLogger.Object);

        // Act
        var result = await svc.CreateAndPushAsync(
            userId,
            NotificationType.TaskAssignedPending,
            "Bạn được giao task \"Fix bug\".",
            taskId: Guid.NewGuid(),
            redirectUrl: "/Tasks/Details/123");

        // Assert
        Assert.NotEqual(Guid.Empty, result);

        var saved = await db.UserNotifications.FirstOrDefaultAsync(n => n.Id == result);
        Assert.NotNull(saved);
        Assert.Equal(userId, saved.UserId);
        Assert.Equal(NotificationType.TaskAssignedPending, saved.Type);
        Assert.Equal("Bạn được giao task \"Fix bug\".", saved.Message);
        Assert.False(saved.IsRead);
        Assert.NotNull(saved.RedirectUrl);

        // SignalR push was called
        mockHub.Verify(x => x.Clients.User(userId), Times.Once);
    }

    [Fact]
    public async Task CreateAndPushAsync_SetsAllEntityFields()
    {
        var db = CreateDb();
        var userId = "user-2";
        var workspaceId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var taskId = Guid.NewGuid();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "u2@test.com", Email = "u2@test.com" });
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Test WS" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = userId });
        await db.SaveChangesAsync();

        var mockHub = CreateMockHubContext();
        var svc = new NotificationService(db, mockHub.Object, Mock.Of<ILogger<NotificationService>>());

        var result = await svc.CreateAndPushAsync(
            userId,
            NotificationType.TaskEvaluated,
            "Task evaluated: 8/10",
            workspaceId,
            projectId,
            taskId,
            "/Tasks/Details/1");

        var saved = await db.UserNotifications.FirstAsync(n => n.Id == result);
        Assert.Equal(workspaceId, saved.WorkspaceId);
        Assert.Equal(projectId, saved.ProjectId);
        Assert.Equal(taskId, saved.TaskId);
        Assert.Equal(NotificationType.TaskEvaluated, saved.Type);
    }

    [Fact]
    public async Task CreateAndPushAsync_EmptyUserId_ReturnsEmptyGuidAndDoesNotSave()
    {
        var db = CreateDb();
        var mockHub = CreateMockHubContext();
        var svc = new NotificationService(db, mockHub.Object, Mock.Of<ILogger<NotificationService>>());

        var result = await svc.CreateAndPushAsync(
            "",
            NotificationType.TaskAssignedPending,
            "Test");

        Assert.Equal(Guid.Empty, result);
        Assert.Equal(0, await db.UserNotifications.CountAsync());

        mockHub.Verify(x => x.Clients.User(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateAndPushAsync_WhitespaceUserId_ReturnsEmptyGuidAndDoesNotSave()
    {
        var db = CreateDb();
        var svc = new NotificationService(db, CreateMockHubContext().Object, Mock.Of<ILogger<NotificationService>>());

        var result = await svc.CreateAndPushAsync(
            "   ",
            NotificationType.TaskAssignedPending,
            "Test");

        Assert.Equal(Guid.Empty, result);
        Assert.Equal(0, await db.UserNotifications.CountAsync());
    }

    // ============================================================
    // CreateAndPushAsync — Workspace membership validation
    // ============================================================

    [Fact]
    public async Task CreateAndPushAsync_WithWorkspace_UserIsNotMember_SkipsAndReturnsEmpty()
    {
        var db = CreateDb();
        var userId = "outsider";
        var workspaceId = Guid.NewGuid();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "out@test.com", Email = "out@test.com" });
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Private WS" });
        // Note: no WorkspaceMember entry for userId
        await db.SaveChangesAsync();

        var mockHub = CreateMockHubContext();
        var svc = new NotificationService(db, mockHub.Object, Mock.Of<ILogger<NotificationService>>());

        var result = await svc.CreateAndPushAsync(
            userId,
            NotificationType.ProjectCreated,
            "Project created",
            workspaceId);

        Assert.Equal(Guid.Empty, result);
        Assert.Equal(0, await db.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task CreateAndPushAsync_WithWorkspace_UserIsMember_Saves()
    {
        var db = CreateDb();
        var userId = "member-1";
        var workspaceId = Guid.NewGuid();

        db.Users.Add(new ApplicationUser { Id = userId, UserName = "m1@test.com", Email = "m1@test.com" });
        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Team WS" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = userId });
        await db.SaveChangesAsync();

        var mockHub = CreateMockHubContext();
        var svc = new NotificationService(db, mockHub.Object, Mock.Of<ILogger<NotificationService>>());

        var result = await svc.CreateAndPushAsync(
            userId,
            NotificationType.ProjectDeleted,
            "Project deleted",
            workspaceId);

        Assert.NotEqual(Guid.Empty, result);
        Assert.Equal(1, await db.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task CreateAndPushAsync_WithoutWorkspaceId_SkipsMembershipCheck()
    {
        var db = CreateDb();
        var userId = "loner";
        db.Users.Add(new ApplicationUser { Id = userId, UserName = "loner@test.com", Email = "loner@test.com" });
        await db.SaveChangesAsync();

        var mockHub = CreateMockHubContext();
        var svc = new NotificationService(db, mockHub.Object, Mock.Of<ILogger<NotificationService>>());

        // workspaceId = null → skips membership validation
        var result = await svc.CreateAndPushAsync(
            userId,
            NotificationType.RegistrationPendingPm,
            "PM registration pending",
            workspaceId: null);

        Assert.NotEqual(Guid.Empty, result);
        Assert.Equal(1, await db.UserNotifications.CountAsync());
    }

    // ============================================================
    // CreateAndPushAsync — SignalR failure handling (Exception)
    // ============================================================

    [Fact]
    public async Task CreateAndPushAsync_SignalRFails_StillSavesToDatabase()
    {
        var db = CreateDb();
        var userId = "user-signalr-fail";
        db.Users.Add(new ApplicationUser { Id = userId, UserName = "fail@test.com", Email = "fail@test.com" });
        await db.SaveChangesAsync();

        var mockHub = CreateMockHubContext();
        var mockClient = mockHub.Object.Clients.User(userId);
        mockClient.Setup(c => c.SendAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("SignalR connection lost"));

        var svc = new NotificationService(db, mockHub.Object, Mock.Of<ILogger<NotificationService>>());

        // Should NOT throw — SignalR failures are caught internally
        var result = await svc.CreateAndPushAsync(
            userId,
            NotificationType.RoleChanged,
            "Role changed");

        // DB save still succeeded
        Assert.NotEqual(Guid.Empty, result);
        Assert.Equal(1, await db.UserNotifications.CountAsync());
    }

    [Fact]
    public async Task CreateAndPushAsync_SignalRFails_DoesNotRollbackDbTransaction()
    {
        var db = CreateDb();
        var userId = "user-no-rollback";
        db.Users.Add(new ApplicationUser { Id = userId, UserName = "nr@test.com", Email = "nr@test.com" });
        await db.SaveChangesAsync();

        var mockHub = CreateMockHubContext();
        var mockClient = mockHub.Object.Clients.User(userId);
        mockClient.Setup(c => c.SendAsync(
                It.IsAny<string>(),
                It.IsAny<object>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Network error"));

        var svc = new NotificationService(db, mockHub.Object, Mock.Of<ILogger<NotificationService>>());

        var notificationId = await svc.CreateAndPushAsync(
            userId,
            NotificationType.WorkspaceInvite,
            "You are invited");

        var saved = await db.UserNotifications.FirstOrDefaultAsync(n => n.Id == notificationId);
        Assert.NotNull(saved);
        Assert.Equal(NotificationType.WorkspaceInvite, saved.Type);
    }

    // ============================================================
    // CreateAndPushAsync — NotificationType coverage
    // ============================================================

    [Theory]
    [InlineData(NotificationType.TaskAssignedPending, "New task assigned")]
    [InlineData(NotificationType.TaskRejected, "Task rejected with reason")]
    [InlineData(NotificationType.TaskAccepted, "Task accepted")]
    [InlineData(NotificationType.TaskDoneNeedsEvaluation, "Task needs evaluation")]
    [InlineData(NotificationType.TaskEvaluated, "Task evaluated")]
    [InlineData(NotificationType.RoleChanged, "Role changed")]
    [InlineData(NotificationType.WorkspaceInvite, "Workspace invitation")]
    [InlineData(NotificationType.DeadlineReminder, "Deadline in 24h")]
    [InlineData(NotificationType.ProjectCreated, "Project created")]
    [InlineData(NotificationType.ProjectDeleted, "Project deleted")]
    [InlineData(NotificationType.LevelChanged, "Level changed")]
    [InlineData(NotificationType.TaskKanbanMoved, "Task moved on Kanban")]
    [InlineData(NotificationType.TaskCommentAdded, "New comment")]
    [InlineData(NotificationType.ProjectUpdated, "Project updated")]
    [InlineData(NotificationType.InviteAccepted, "Invite accepted")]
    [InlineData(NotificationType.InviteRejected, "Invite rejected")]
    [InlineData(NotificationType.RegistrationPendingPm, "PM registration pending")]
    [InlineData(NotificationType.WorkspacePmRoleRequest, "Workspace role request")]
    [InlineData(NotificationType.RemovedFromWorkspace, "Removed from workspace")]
    [InlineData(NotificationType.LevelAdjustmentRequest, "Level adjustment request")]
    public async Task CreateAndPushAsync_AllNotificationTypes_SavesCorrectly(
        NotificationType type,
        string message)
    {
        var db = CreateDb();
        var userId = $"user-{type}";
        db.Users.Add(new ApplicationUser { Id = userId, UserName = $"{userId}@test.com", Email = $"{userId}@test.com" });
        await db.SaveChangesAsync();

        var mockHub = CreateMockHubContext();
        var svc = new NotificationService(db, mockHub.Object, Mock.Of<ILogger<NotificationService>>());

        var result = await svc.CreateAndPushAsync(userId, type, message);

        Assert.NotEqual(Guid.Empty, result);
        var saved = await db.UserNotifications.FirstAsync(n => n.Id == result);
        Assert.Equal(type, saved.Type);
        Assert.Equal(message, saved.Message);
        Assert.False(saved.IsRead);
    }
}
