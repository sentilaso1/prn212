using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using WorkFlowPro.Areas.Identity.Pages.Account;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;

namespace WorkFlowPro.Tests;

public class SignInModelTests
{
    private static WorkFlowProDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<WorkFlowProDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var mockWorkspaceSvc = new Mock<ICurrentWorkspaceService>();
        mockWorkspaceSvc.Setup(x => x.CurrentWorkspaceId).Returns((Guid?)null);

        var mockUserAccessor = new Mock<ICurrentUserAccessor>();
        mockUserAccessor.Setup(x => x.UserId).Returns((string?)null);

        return new WorkFlowProDbContext(options, mockWorkspaceSvc.Object, mockUserAccessor.Object);
    }

    private static LoginModel WithPageContext(LoginModel model)
    {
        var authService = new Mock<IAuthenticationService>();
        authService.Setup(x => x.SignInAsync(
                It.IsAny<HttpContext>(),
                It.IsAny<string>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>()))
            .Returns(Task.CompletedTask);

        var services = new ServiceCollection();
        services.AddSingleton(authService.Object);
        var sp = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext { RequestServices = sp };
        model.PageContext = new PageContext { HttpContext = httpContext };
        return model;
    }

    private static Mock<UserManager<ApplicationUser>> CreateMockUserManager(ApplicationUser? user = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        userManager.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(user);

        userManager.Setup(x => x.GetLockoutEndDateAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(user?.LockoutEnd);

        return userManager;
    }

    private static Mock<SignInManager<ApplicationUser>> CreateMockSignInManager(
        ApplicationUser? user = null,
        Microsoft.AspNetCore.Identity.SignInResult? signInResult = null)
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        var signInManager = new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            null!, null!, null!, null!);

        signInManager.Setup(x => x.CheckPasswordSignInAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<string>(),
                It.IsAny<bool>()))
            .ReturnsAsync(signInResult ?? Microsoft.AspNetCore.Identity.SignInResult.Success);

        signInManager.Setup(x => x.CreateUserPrincipalAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync(() =>
            {
                var claims = new List<Claim>();
                if (user != null && user.IsPlatformAdmin)
                    claims.Add(new Claim("platform_role", "admin"));
                return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
            });

        signInManager.Setup(x => x.SignInAsync(
                It.IsAny<ApplicationUser>(),
                It.IsAny<bool>(),
                It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        return signInManager;
    }

    // ==========================================
    // UC-02 Main Flow
    // ==========================================

    [Fact]
    public async Task OnPostAsync_ValidCredentials_UserHasNoWorkspaces_RedirectsToWorkspaces()
    {
        var db = CreateInMemoryDb();
        var user = new ApplicationUser
        {
            Id = "user1",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            AccountStatus = AccountStatus.Approved
        };

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            CreateMockSignInManager(user).Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "user@gmail.com",
            Password = "Password1",
            RememberMe = false
        };

        var result = await model.OnPostAsync();

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Workspaces", redirect.Url);
    }

    [Fact]
    public async Task OnPostAsync_ValidCredentials_UserHasOneWorkspace_RedirectsToThatWorkspace()
    {
        var db = CreateInMemoryDb();
        var wsId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace { Id = wsId, Name = "Test WS" });
        var user = new ApplicationUser
        {
            Id = "user1",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            AccountStatus = AccountStatus.Approved
        };
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            UserId = user.Id,
            WorkspaceId = wsId,
            Role = WorkspaceMemberRole.Member,
            JoinedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            CreateMockSignInManager(user).Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "user@gmail.com",
            Password = "Password1"
        };

        var result = await model.OnPostAsync();

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal($"/Workspaces?workspaceId={wsId}", redirect.Url);
    }

    [Fact]
    public async Task OnPostAsync_ValidCredentials_UserHasMultipleWorkspaces_RedirectsToWorkspaceSwitcher()
    {
        var db = CreateInMemoryDb();
        var ws1 = Guid.NewGuid();
        var ws2 = Guid.NewGuid();
        db.Workspaces.Add(new Workspace { Id = ws1, Name = "WS 1" });
        db.Workspaces.Add(new Workspace { Id = ws2, Name = "WS 2" });
        var user = new ApplicationUser
        {
            Id = "user1",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            AccountStatus = AccountStatus.Approved
        };
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            UserId = user.Id, WorkspaceId = ws1, Role = WorkspaceMemberRole.Member,
            JoinedAtUtc = DateTime.UtcNow.AddHours(-2)
        });
        db.WorkspaceMembers.Add(new WorkspaceMember
        {
            UserId = user.Id, WorkspaceId = ws2, Role = WorkspaceMemberRole.Member,
            JoinedAtUtc = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            CreateMockSignInManager(user).Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "user@gmail.com",
            Password = "Password1"
        };

        var result = await model.OnPostAsync();

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Workspaces", redirect.Url);
    }

    [Fact]
    public async Task OnPostAsync_PlatformAdmin_UserHasNoWorkspaces_RedirectsToWorkspaces()
    {
        var db = CreateInMemoryDb();
        var user = new ApplicationUser
        {
            Id = "admin1",
            UserName = "admin@gmail.com",
            Email = "admin@gmail.com",
            AccountStatus = AccountStatus.Approved,
            IsPlatformAdmin = true
        };

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            CreateMockSignInManager(user).Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "admin@gmail.com",
            Password = "Password1"
        };

        var result = await model.OnPostAsync();

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Workspaces", redirect.Url);
    }

    // ==========================================
    // UC-02 Step 2 - Invalid credentials
    // ==========================================

    [Fact]
    public async Task OnPostAsync_EmailNotFound_ReturnsInvalidLoginAttempt()
    {
        var db = CreateInMemoryDb();
        var model = WithPageContext(new LoginModel(
            CreateMockUserManager().Object,
            CreateMockSignInManager().Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "notfound@gmail.com",
            Password = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e => e.ErrorMessage.Contains("Invalid")));
    }

    [Fact]
    public async Task OnPostAsync_WrongPassword_ReturnsInvalidLoginAttempt()
    {
        var db = CreateInMemoryDb();
        var user = new ApplicationUser
        {
            Id = "user1",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            AccountStatus = AccountStatus.Approved
        };

        var signInManager = CreateMockSignInManager(user,
            Microsoft.AspNetCore.Identity.SignInResult.Failed);

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            signInManager.Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "user@gmail.com",
            Password = "WrongPassword"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e => e.ErrorMessage.Contains("Invalid")));
    }

    // ==========================================
    // UC-02 Step 2 - Account status checks
    // ==========================================

    [Fact]
    public async Task OnPostAsync_PmAccount_PendingApproval_ReturnsAwaitingApprovalMessage()
    {
        var db = CreateInMemoryDb();
        var user = new ApplicationUser
        {
            Id = "pm1",
            UserName = "pm@company.com",
            Email = "pm@company.com",
            AccountStatus = AccountStatus.PendingApproval,
            AwaitingPmWorkspaceApproval = true
        };

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            CreateMockSignInManager(user).Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "pm@company.com",
            Password = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e =>
                e.ErrorMessage.Contains("chờ") && e.ErrorMessage.Contains("Admin")));
    }

    [Fact]
    public async Task OnPostAsync_AccountRejected_ReturnsRejectedMessage()
    {
        var db = CreateInMemoryDb();
        var user = new ApplicationUser
        {
            Id = "user1",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            AccountStatus = AccountStatus.Rejected
        };

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            CreateMockSignInManager(user).Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "user@gmail.com",
            Password = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e => e.ErrorMessage.Contains("từ chối")));
    }

    // ==========================================
    // UC-02 Exception - Account lockout
    // ==========================================

    [Fact]
    public async Task OnPostAsync_AccountLockedOut_AfterFiveFailedAttempts_ReturnsLockoutMessage()
    {
        var db = CreateInMemoryDb();
        var lockoutEnd = DateTime.UtcNow.AddMinutes(15);
        var user = new ApplicationUser
        {
            Id = "user1",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            AccountStatus = AccountStatus.Approved,
            LockoutEnd = lockoutEnd
        };

        var signInManager = CreateMockSignInManager(user,
            Microsoft.AspNetCore.Identity.SignInResult.LockedOut);

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            signInManager.Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "user@gmail.com",
            Password = "WrongPassword"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e =>
                e.ErrorMessage.Contains("khóa") || e.ErrorMessage.Contains("15")));
    }

    // ==========================================
    // UC-02 RequiresTwoFactor & IsNotAllowed
    // ==========================================

    [Fact]
    public async Task OnPostAsync_RequiresTwoFactor_ReturnsTwoFactorMessage()
    {
        var db = CreateInMemoryDb();
        var user = new ApplicationUser
        {
            Id = "user1",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            AccountStatus = AccountStatus.Approved
        };

        var signInManager = CreateMockSignInManager(user,
            Microsoft.AspNetCore.Identity.SignInResult.TwoFactorRequired);

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            signInManager.Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "user@gmail.com",
            Password = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e =>
                e.ErrorMessage.Contains("Two-factor")));
    }

    [Fact]
    public async Task OnPostAsync_IsNotAllowed_ReturnsNotAllowedMessage()
    {
        var db = CreateInMemoryDb();
        var user = new ApplicationUser
        {
            Id = "user1",
            UserName = "user@gmail.com",
            Email = "user@gmail.com",
            AccountStatus = AccountStatus.Approved
        };

        var signInManager = CreateMockSignInManager(user,
            Microsoft.AspNetCore.Identity.SignInResult.NotAllowed);

        var model = WithPageContext(new LoginModel(
            CreateMockUserManager(user).Object,
            signInManager.Object,
            db));

        model.Input = new LoginModel.InputModel
        {
            Email = "user@gmail.com",
            Password = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e =>
                e.ErrorMessage.Contains("not allowed", StringComparison.OrdinalIgnoreCase)));
    }

    // ==========================================
    // UC-02 OnGet
    // ==========================================

    [Fact]
    public void OnGet_ReturnsPage()
    {
        var model = WithPageContext(new LoginModel(
            CreateMockUserManager().Object,
            CreateMockSignInManager().Object,
            CreateInMemoryDb()));

        model.OnGet();

        Assert.NotNull(model.PageContext);
    }
}
