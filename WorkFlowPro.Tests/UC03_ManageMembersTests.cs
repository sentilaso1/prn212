using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;
using WorkFlowPro.Services;

namespace WorkFlowPro.Tests;

/// <summary>UC-03: Manage Members
/// Path A — PM invites Member
/// Path B — PM removes Member
/// Path C — PM changes Member sub-role
/// Alternative sequences and exceptions
/// </summary>
public class UC03_ManageMembersTests
{
    // ============================================================
    // Shared helpers
    // ============================================================

    private static WorkFlowProDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<WorkFlowProDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        var mockWorkspaceSvc = new Mock<ICurrentWorkspaceService>();
        mockWorkspaceSvc.Setup(x => x.CurrentWorkspaceId).Returns((Guid?)null);

        var mockUserAccessor = new Mock<ICurrentUserAccessor>();
        mockUserAccessor.Setup(x => x.UserId).Returns((string?)null);

        return new WorkFlowProDbContext(options, mockWorkspaceSvc.Object, mockUserAccessor.Object);
    }

    private static Mock<UserManager<ApplicationUser>> CreateMockUserManager(ApplicationUser? user = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var um = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        um.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);
        um.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        return um;
    }

    private static Mock<SignInManager<ApplicationUser>> CreateMockSignInManager()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        var sm = new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);

        sm.Setup(x => x.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        sm.Setup(x => x.CreateUserPrincipalAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(new System.Security.Claims.ClaimsPrincipal());

        return sm;
    }

    private static IConfiguration BuildConfig() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

    private static Mock<INotificationService> CreateMockNotificationService()
    {
        return new Mock<INotificationService>();
    }

    private static Mock<IHubContext<TaskHub>> CreateMockTaskHub()
    {
        return new Mock<IHubContext<TaskHub>>();
    }

    private static Mock<IAdminAuditService> CreateMockAuditService()
    {
        return new Mock<IAdminAuditService>();
    }

    // ============================================================
    // UC-03 | Path A — Invite Members
    // ============================================================

    // --- InviteMembersAsync ---

    [Fact]
    public async Task InviteMembers_PathA_NormalFlow_CreatesTokenAndSendsNotification()
    {
        // Arrange
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";
        var inviteeEmail = "newmember@gmail.com";
        var invitee = new ApplicationUser { Id = "invitee1", Email = inviteeEmail, UserName = inviteeEmail };
        var workspace = new Workspace { Id = workspaceId, Name = "Alpha" };
        var pm = new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM };

        db.Workspaces.Add(workspace);
        db.WorkspaceMembers.Add(pm);
        db.Users.Add(invitee);
        await db.SaveChangesAsync();

        var notifSvc = CreateMockNotificationService();
        var userManager = CreateMockUserManager(invitee);
        var httpCtx = new DefaultHttpContext();

        var svc = new InvitationService(
            db, userManager.Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(),
            BuildConfig(), Mock.Of<ILogger<InvitationService>>(), notifSvc.Object);

        // Act
        var result = await svc.InviteMembersAsync(workspaceId, inviteeEmail, WorkspaceMemberRole.Member, "DEV");

        // Assert
        Assert.True(result.Success);
        Assert.Empty(result.Errors);
        Assert.Single(result.DebugAcceptLinks);

        var token = await db.WorkspaceInviteTokens.FirstOrDefaultAsync();
        Assert.NotNull(token);
        Assert.Equal(inviteeEmail.ToLower(), token.Email.ToLower());
        Assert.Equal(WorkspaceMemberRole.Member, token.Role);
        Assert.Equal("DEV", token.SubRole);
        Assert.Equal(InviteStatus.Pending, token.Status);

        notifSvc.Verify(x => x.CreateAndPushAsync(
            invitee.Id,
            NotificationType.WorkspaceInvite,
            It.Is<string>(m => m.Contains("Alpha") && m.Contains("DEV")),
            null, null, null, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InviteMembers_PathA_AlreadyMember_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var existingUser = new ApplicationUser { Id = "u1", Email = "member@gmail.com", UserName = "member@gmail.com" };
        var existingMember = new WorkspaceMember { WorkspaceId = workspaceId, UserId = "u1", Role = WorkspaceMemberRole.Member };

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(existingMember);
        db.Users.Add(existingUser);
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager(existingUser).Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.InviteMembersAsync(workspaceId, "member@gmail.com", WorkspaceMemberRole.Member, "DEV");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("đã là thành viên"));
    }

    [Fact]
    public async Task InviteMembers_PathA_EmailExpiredStep_InvalidToken_ReturnsExpiredError()
    {
        var db = CreateInMemoryDb();
        var user = new ApplicationUser { Id = "u1", Email = "user@gmail.com", UserName = "user@gmail.com" };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager(user).Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        // Build an expired token: we simulate by using AcceptInvite after expiry
        // We need to create a token with past expiry
        // Access the private method indirectly via AcceptInvite: we can insert an expired token manually

        // Insert a token that has already expired
        var expiredToken = new WorkspaceInviteToken
        {
            WorkspaceId = Guid.NewGuid(),
            Email = "user@gmail.com",
            TokenHash = "expiredhash",
            Role = WorkspaceMemberRole.Member,
            SubRole = "DEV",
            Status = InviteStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-8),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-1)
        };
        db.WorkspaceInviteTokens.Add(expiredToken);

        // We need the hash to match — let's use a helper to compute it
        var tokenPlain = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var hash = ComputeSha256(tokenPlain);
        expiredToken.TokenHash = hash;
        await db.SaveChangesAsync();

        var result = await svc.AcceptInviteAsync(tokenPlain);

        Assert.False(result.Success);
        Assert.Contains("hết hạn", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task InviteMembers_PathA_NoExistingAccount_SendsEmailOnly()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "pm1", Role = WorkspaceMemberRole.PM });
        await db.SaveChangesAsync();

        var notifSvc = CreateMockNotificationService();
        var userManager = CreateMockUserManager(null); // no existing user

        var svc = new InvitationService(
            db, userManager.Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), notifSvc.Object);

        var result = await svc.InviteMembersAsync(workspaceId, "newuser@gmail.com", WorkspaceMemberRole.Member, "BA");

        Assert.True(result.Success);

        // No notification because user doesn't exist
        notifSvc.Verify(x => x.CreateAndPushAsync(
            It.IsAny<string>(), NotificationType.WorkspaceInvite, It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        // But token is created
        var token = await db.WorkspaceInviteTokens.FirstOrDefaultAsync();
        Assert.NotNull(token);
        Assert.Equal("newuser@gmail.com", token.Email);
    }

    [Fact]
    public async Task InviteMembers_InvalidSubRole_ReturnsError()
    {
        var db = CreateInMemoryDb();
        db.Workspaces.Add(new Workspace { Id = Guid.NewGuid(), Name = "Alpha" });
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.InviteMembersAsync(Guid.NewGuid(), "test@gmail.com", WorkspaceMemberRole.Member, "Hacker");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("SubRole không hợp lệ"));
    }

    [Fact]
    public async Task InviteMembers_EmptyEmail_ReturnsError()
    {
        var db = CreateInMemoryDb();
        db.Workspaces.Add(new Workspace { Id = Guid.NewGuid(), Name = "Alpha" });
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.InviteMembersAsync(Guid.NewGuid(), "", WorkspaceMemberRole.Member, "DEV");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("ít nhất 1 email"));
    }

    [Fact]
    public async Task InviteMembers_InvalidEmail_ReturnsError()
    {
        var db = CreateInMemoryDb();
        db.Workspaces.Add(new Workspace { Id = Guid.NewGuid(), Name = "Alpha" });
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.InviteMembersAsync(Guid.NewGuid(), "notanemail", WorkspaceMemberRole.Member, "DEV");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("không hợp lệ"));
    }

    [Fact]
    public async Task InviteMembers_PMReachesMaxLimit_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        // Add 3 PMs (MaxPmsPerWorkspace = 3)
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "pm1", Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "pm2", Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "pm3", Role = WorkspaceMemberRole.PM });
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.InviteMembersAsync(workspaceId, "newpm@gmail.com", WorkspaceMemberRole.PM, "DEV");

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("đã đủ"));
    }

    // --- AcceptInviteAsync ---

    [Fact]
    public async Task AcceptInvite_UserNotExists_CreatesAccountAndJoinsWorkspace()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "pm1", Role = WorkspaceMemberRole.PM });
        await db.SaveChangesAsync();

        var tokenPlain = CreateTokenPlain();
        var tokenHash = ComputeSha256(tokenPlain);

        db.WorkspaceInviteTokens.Add(new WorkspaceInviteToken
        {
            WorkspaceId = workspaceId,
            Email = "newuser@gmail.com",
            TokenHash = tokenHash,
            Role = WorkspaceMemberRole.Member,
            SubRole = "DEV",
            Status = InviteStatus.Pending,
            AcceptUrl = $"/Invite/Accept?token={Uri.EscapeDataString(tokenPlain)}",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager(null); // user doesn't exist
        var signInMgr = CreateMockSignInManager();
        var notifSvc = CreateMockNotificationService();
        var httpCtx = new DefaultHttpContext();
        var svc = new InvitationService(
            db, userManager.Object, signInMgr.Object,
            Mock.Of<IHttpContextAccessor>(),
            BuildConfig(), Mock.Of<ILogger<InvitationService>>(), notifSvc.Object);

        var result = await svc.AcceptInviteAsync(tokenPlain);

        Assert.True(result.Success);
        Assert.Equal(workspaceId, result.WorkspaceId);

        var member = await db.WorkspaceMembers.FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId != "pm1");
        Assert.NotNull(member);
        Assert.Equal(WorkspaceMemberRole.Member, member.Role);
        Assert.Equal("DEV", member.SubRole);

        userManager.Verify(x => x.CreateAsync(
            It.Is<ApplicationUser>(u => u.Email == "newuser@gmail.com" && u.EmailConfirmed),
            It.IsAny<string>()), Times.Once);

        var token = await db.WorkspaceInviteTokens.FirstAsync();
        Assert.Equal(InviteStatus.Accepted, token.Status);
        Assert.NotNull(token.UsedAtUtc);
    }

    [Fact]
    public async Task AcceptInvite_UserAlreadyMember_UpdatesRoleAndSubRole()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var existingUser = new ApplicationUser { Id = "existing1", Email = "existing@gmail.com", UserName = "existing@gmail.com" };

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "pm1", Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "existing1", Role = WorkspaceMemberRole.Member, SubRole = "BA" });
        db.Users.Add(existingUser);
        await db.SaveChangesAsync();

        var tokenPlain = CreateTokenPlain();
        var tokenHash = ComputeSha256(tokenPlain);

        db.WorkspaceInviteTokens.Add(new WorkspaceInviteToken
        {
            WorkspaceId = workspaceId,
            Email = "existing@gmail.com",
            TokenHash = tokenHash,
            Role = WorkspaceMemberRole.Member,
            SubRole = "DEV",
            Status = InviteStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager(existingUser);
        var httpCtx = new DefaultHttpContext();
        var svc = new InvitationService(
            db, userManager.Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(),
            BuildConfig(), Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.AcceptInviteAsync(tokenPlain);

        Assert.True(result.Success);

        var member = await db.WorkspaceMembers.FirstAsync(m => m.UserId == "existing1");
        Assert.Equal("DEV", member.SubRole);
    }

    [Fact]
    public async Task AcceptInvite_UsedToken_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        await db.SaveChangesAsync();

        var tokenPlain = CreateTokenPlain();
        var tokenHash = ComputeSha256(tokenPlain);

        db.WorkspaceInviteTokens.Add(new WorkspaceInviteToken
        {
            WorkspaceId = workspaceId,
            Email = "user@gmail.com",
            TokenHash = tokenHash,
            Role = WorkspaceMemberRole.Member,
            Status = InviteStatus.Accepted,
            UsedAtUtc = DateTime.UtcNow.AddHours(-1),
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.AcceptInviteAsync(tokenPlain);

        Assert.False(result.Success);
        Assert.Contains("đã được xử lý", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task AcceptInvite_NonExistentToken_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.AcceptInviteAsync("nonexistent-token");

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task AcceptInvite_EmptyToken_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.AcceptInviteAsync("");

        Assert.False(result.Success);
        Assert.Contains("Token không hợp lệ", result.ErrorMessage ?? "");
    }

    // --- RejectInviteAsync ---

    [Fact]
    public async Task RejectInvite_NormalFlow_SetsStatusRejectedAndNotifiesPM()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        await db.SaveChangesAsync();

        var tokenPlain = CreateTokenPlain();
        var tokenHash = ComputeSha256(tokenPlain);

        db.WorkspaceInviteTokens.Add(new WorkspaceInviteToken
        {
            WorkspaceId = workspaceId,
            Email = "user@gmail.com",
            TokenHash = tokenHash,
            Role = WorkspaceMemberRole.Member,
            Status = InviteStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var notifSvc = CreateMockNotificationService();
        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), notifSvc.Object);

        var result = await svc.RejectInviteAsync(tokenPlain);

        Assert.True(result.Success);

        var token = await db.WorkspaceInviteTokens.FirstAsync();
        Assert.Equal(InviteStatus.Rejected, token.Status);
        Assert.NotNull(token.UsedAtUtc);

        notifSvc.Verify(x => x.CreateAndPushAsync(
            pmUserId,
            NotificationType.InviteRejected,
            It.Is<string>(m => m.Contains("đã từ chối")),
            workspaceId, null, null, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectInvite_ExpiredToken_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        await db.SaveChangesAsync();

        var tokenPlain = CreateTokenPlain();
        var tokenHash = ComputeSha256(tokenPlain);

        db.WorkspaceInviteTokens.Add(new WorkspaceInviteToken
        {
            WorkspaceId = workspaceId,
            Email = "user@gmail.com",
            TokenHash = tokenHash,
            Role = WorkspaceMemberRole.Member,
            Status = InviteStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-8),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.RejectInviteAsync(tokenPlain);

        Assert.False(result.Success);
        Assert.Contains("hết hạn", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task RejectInvite_AlreadyUsedToken_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        await db.SaveChangesAsync();

        var tokenPlain = CreateTokenPlain();
        var tokenHash = ComputeSha256(tokenPlain);

        db.WorkspaceInviteTokens.Add(new WorkspaceInviteToken
        {
            WorkspaceId = workspaceId,
            Email = "user@gmail.com",
            TokenHash = tokenHash,
            Role = WorkspaceMemberRole.Member,
            Status = InviteStatus.Accepted,
            UsedAtUtc = DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow.AddDays(-1),
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.RejectInviteAsync(tokenPlain);

        Assert.False(result.Success);
        Assert.Contains("đã được xử lý", result.ErrorMessage ?? "");
    }

    // --- GetInviteInfoAsync ---

    [Fact]
    public async Task GetInviteInfo_ValidToken_ReturnsInviteDetails()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Test Workspace" });
        await db.SaveChangesAsync();

        var tokenPlain = CreateTokenPlain();
        var tokenHash = ComputeSha256(tokenPlain);

        db.WorkspaceInviteTokens.Add(new WorkspaceInviteToken
        {
            WorkspaceId = workspaceId,
            Email = "user@gmail.com",
            TokenHash = tokenHash,
            Role = WorkspaceMemberRole.Member,
            SubRole = "Designer",
            Status = InviteStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(1)
        });
        await db.SaveChangesAsync();

        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.GetInviteInfoAsync(tokenPlain);

        Assert.NotNull(result);
        Assert.Equal("Test Workspace", result.WorkspaceName);
        Assert.Equal("user@gmail.com", result.Email);
        Assert.Equal(WorkspaceMemberRole.Member, result.Role);
        Assert.Equal("Designer", result.SubRole);
        Assert.Equal(InviteStatus.Pending, result.Status);
    }

    [Fact]
    public async Task GetInviteInfo_InvalidToken_ReturnsNull()
    {
        var db = CreateInMemoryDb();
        var svc = new InvitationService(
            db, CreateMockUserManager().Object, CreateMockSignInManager().Object,
            Mock.Of<IHttpContextAccessor>(), BuildConfig(),
            Mock.Of<ILogger<InvitationService>>(), CreateMockNotificationService().Object);

        var result = await svc.GetInviteInfoAsync("invalid-token");

        Assert.Null(result);
    }

    // ============================================================
    // UC-03 | Path B — Remove Member
    // ============================================================

    [Fact]
    public async Task RemoveMember_PathB_NormalFlow_RemovesMemberAndReturnsSuccess()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";
        var targetUserId = "member1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = targetUserId, Role = WorkspaceMemberRole.Member });
        db.Users.Add(new ApplicationUser { Id = targetUserId, Email = "member@gmail.com", UserName = "member@gmail.com" });
        await db.SaveChangesAsync();

        var notifSvc = CreateMockNotificationService();
        var taskHub = CreateMockTaskHub();
        var audit = CreateMockAuditService();

        var svc = new RoleManagementService(db, notifSvc.Object, taskHub.Object, audit.Object,
            Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.RemoveMemberFromWorkspaceAsync(workspaceId, pmUserId, targetUserId, "No longer needed");

        Assert.True(result.Success);

        var member = await db.WorkspaceMembers.FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId);
        Assert.Null(member);

        notifSvc.Verify(x => x.CreateAndPushAsync(
            targetUserId,
            NotificationType.RemovedFromWorkspace,
            It.Is<string>(m => m.Contains("Alpha") && m.Contains("No longer needed")),
            null, null, null, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemoveMember_SelfRemove_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        await db.SaveChangesAsync();

        var svc = new RoleManagementService(
            db, CreateMockNotificationService().Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.RemoveMemberFromWorkspaceAsync(workspaceId, pmUserId, pmUserId, "trying to self-remove");

        Assert.False(result.Success);
        Assert.Contains("chính bạn", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task RemoveMember_CannotRemovePM_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pm1 = "pm1";
        var pm2 = "pm2";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pm1, Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pm2, Role = WorkspaceMemberRole.PM });
        await db.SaveChangesAsync();

        var svc = new RoleManagementService(
            db, CreateMockNotificationService().Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.RemoveMemberFromWorkspaceAsync(workspaceId, pm1, pm2, "removing another PM");

        Assert.False(result.Success);
        Assert.Contains("Chỉ xóa trực tiếp được thành viên Member", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task RemoveMember_NonExistentMember_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        await db.SaveChangesAsync();

        var svc = new RoleManagementService(
            db, CreateMockNotificationService().Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.RemoveMemberFromWorkspaceAsync(workspaceId, pmUserId, "nonexistent", "test");

        Assert.False(result.Success);
        Assert.Contains("không tồn tại", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task RemoveMember_NonPM_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var regularMember = "member1";
        var targetUserId = "member2";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = regularMember, Role = WorkspaceMemberRole.Member });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = targetUserId, Role = WorkspaceMemberRole.Member });
        await db.SaveChangesAsync();

        var svc = new RoleManagementService(
            db, CreateMockNotificationService().Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.RemoveMemberFromWorkspaceAsync(workspaceId, regularMember, targetUserId, "test");

        Assert.False(result.Success);
        Assert.Contains("quyền", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task RemoveMember_EmptyReason_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";
        var targetUserId = "member1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = targetUserId, Role = WorkspaceMemberRole.Member });
        await db.SaveChangesAsync();

        var svc = new RoleManagementService(
            db, CreateMockNotificationService().Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.RemoveMemberFromWorkspaceAsync(workspaceId, pmUserId, targetUserId, "");

        Assert.False(result.Success);
        Assert.Contains("bắt buộc", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task RemoveMember_AdminCanRemove_ReturnsSuccess()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var adminUserId = "admin1";
        var targetUserId = "member1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = "pm1", Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = targetUserId, Role = WorkspaceMemberRole.Member });
        db.Users.Add(new ApplicationUser { Id = adminUserId, IsPlatformAdmin = true, Email = "admin@local", UserName = "admin@local" });
        await db.SaveChangesAsync();

        var notifSvc = CreateMockNotificationService();
        var svc = new RoleManagementService(
            db, notifSvc.Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.RemoveMemberFromWorkspaceAsync(workspaceId, adminUserId, targetUserId, "admin removal");

        Assert.True(result.Success);

        var member = await db.WorkspaceMembers.FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId);
        Assert.Null(member);
    }

    // ============================================================
    // UC-03 | Path C — Change Sub-Role
    // ============================================================

    [Fact]
    public async Task ChangeSubRole_PathC_NormalFlow_UpdatesSubRoleAndNotifies()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";
        var targetUserId = "member1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = targetUserId, Role = WorkspaceMemberRole.Member, SubRole = "BA" });
        await db.SaveChangesAsync();

        var notifSvc = CreateMockNotificationService();
        var taskHub = CreateMockTaskHub();
        var svc = new RoleManagementService(db, notifSvc.Object, taskHub.Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.ChangeSubRoleAsync(workspaceId, pmUserId, targetUserId, "DEV");

        Assert.True(result.Success);

        var member = await db.WorkspaceMembers.FirstAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId);
        Assert.Equal("DEV", member.SubRole);

        notifSvc.Verify(x => x.CreateAndPushAsync(
            targetUserId,
            NotificationType.RoleChanged,
            It.Is<string>(m => m.Contains("BA") && m.Contains("DEV")),
            workspaceId, null, null, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ChangeSubRole_SameSubRole_ReturnsSuccessWithoutChange()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";
        var targetUserId = "member1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = targetUserId, Role = WorkspaceMemberRole.Member, SubRole = "DEV" });
        await db.SaveChangesAsync();

        var notifSvc = CreateMockNotificationService();
        var svc = new RoleManagementService(
            db, notifSvc.Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.ChangeSubRoleAsync(workspaceId, pmUserId, targetUserId, "DEV");

        Assert.True(result.Success);
        notifSvc.Verify(x => x.CreateAndPushAsync(
            It.IsAny<string>(), It.IsAny<NotificationType>(), It.IsAny<string>(),
            It.IsAny<Guid?>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ChangeSubRole_ClearSubRole_SetsToNull()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";
        var targetUserId = "member1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = targetUserId, Role = WorkspaceMemberRole.Member, SubRole = "DEV" });
        await db.SaveChangesAsync();

        var notifSvc = CreateMockNotificationService();
        var svc = new RoleManagementService(
            db, notifSvc.Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.ChangeSubRoleAsync(workspaceId, pmUserId, targetUserId, null);

        Assert.True(result.Success);

        var member = await db.WorkspaceMembers.FirstAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId);
        Assert.Null(member.SubRole);
    }

    [Fact]
    public async Task ChangeSubRole_NonExistentMember_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        await db.SaveChangesAsync();

        var svc = new RoleManagementService(
            db, CreateMockNotificationService().Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.ChangeSubRoleAsync(workspaceId, pmUserId, "nonexistent", "DEV");

        Assert.False(result.Success);
        Assert.Contains("không tồn tại", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task ChangeSubRole_NonPM_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var regularMember = "member1";
        var targetUserId = "member2";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = regularMember, Role = WorkspaceMemberRole.Member });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = targetUserId, Role = WorkspaceMemberRole.Member });
        await db.SaveChangesAsync();

        var svc = new RoleManagementService(
            db, CreateMockNotificationService().Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var result = await svc.ChangeSubRoleAsync(workspaceId, regularMember, targetUserId, "DEV");

        Assert.False(result.Success);
        Assert.Contains("quyền", result.ErrorMessage ?? "");
    }

    [Fact]
    public async Task ChangeSubRole_SubRoleTooLong_ReturnsError()
    {
        var db = CreateInMemoryDb();
        var workspaceId = Guid.NewGuid();
        var pmUserId = "pm1";
        var targetUserId = "member1";

        db.Workspaces.Add(new Workspace { Id = workspaceId, Name = "Alpha" });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = pmUserId, Role = WorkspaceMemberRole.PM });
        db.WorkspaceMembers.Add(new WorkspaceMember { WorkspaceId = workspaceId, UserId = targetUserId, Role = WorkspaceMemberRole.Member });
        await db.SaveChangesAsync();

        var svc = new RoleManagementService(
            db, CreateMockNotificationService().Object, CreateMockTaskHub().Object,
            CreateMockAuditService().Object, Mock.Of<ILogger<RoleManagementService>>());

        var longSubRole = new string('X', 101);
        var result = await svc.ChangeSubRoleAsync(workspaceId, pmUserId, targetUserId, longSubRole);

        Assert.False(result.Success);
        Assert.Contains("tối đa 100", result.ErrorMessage ?? "");
    }

    // ============================================================
    // Helper methods for token generation
    // ============================================================

    private static string CreateTokenPlain()
    {
        var bytes = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string ComputeSha256(string input)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
