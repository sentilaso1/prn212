using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using WorkFlowPro.Areas.Identity.Pages.Account;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Tests;

public class RegisterModelTests
{
    private static IConfiguration BuildConfig(bool requireEmailConfirmation = false)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:RequireEmailConfirmation"] = requireEmailConfirmation.ToString().ToLower()
            }!)
            .Build();
        return config;
    }

    private static WorkFlowProDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<WorkFlowProDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var mockWorkspaceSvc = new Mock<WorkFlowPro.Auth.ICurrentWorkspaceService>();
        mockWorkspaceSvc.Setup(x => x.CurrentWorkspaceId).Returns((Guid?)null);

        var mockUserAccessor = new Mock<WorkFlowPro.Auth.ICurrentUserAccessor>();
        mockUserAccessor.Setup(x => x.UserId).Returns((string?)null);

        return new WorkFlowProDbContext(options, mockWorkspaceSvc.Object, mockUserAccessor.Object);
    }

    private static RegisterModel WithPageContext(RegisterModel model)
    {
        model.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext()
        };
        return model;
    }

    private static Mock<UserManager<ApplicationUser>> CreateMockUserManager(
        ApplicationUser? existingUser = null,
        IdentityResult? createResult = null)
    {
        var store = new Mock<IUserStore<ApplicationUser>>();
        var userManager = new Mock<UserManager<ApplicationUser>>(
            store.Object, null!, null!, null!, null!, null!, null!, null!, null!);

        userManager.Setup(x => x.FindByEmailAsync(It.IsAny<string>()))
            .ReturnsAsync(existingUser);

        userManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(createResult ?? IdentityResult.Success);

        userManager.Setup(x => x.GenerateEmailConfirmationTokenAsync(It.IsAny<ApplicationUser>()))
            .ReturnsAsync("dummy-token");

        return userManager;
    }

    private static Mock<SignInManager<ApplicationUser>> CreateMockSignInManager()
    {
        var userManager = new Mock<UserManager<ApplicationUser>>(
            Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

        var signInManager = new Mock<SignInManager<ApplicationUser>>(
            userManager.Object,
            Mock.Of<IHttpContextAccessor>(),
            Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            new Mock<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Object,
            Mock.Of<ILogger<SignInManager<ApplicationUser>>>(),
            new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>().Object,
            Mock.Of<Microsoft.AspNetCore.Identity.IUserConfirmation<ApplicationUser>>());

        signInManager.Setup(x => x.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);

        return signInManager;
    }

    // ==========================================
    // OnGet tests
    // ==========================================

    [Fact]
    public async Task OnGet_UnauthenticatedUser_ReturnsPage()
    {
        var model = WithPageContext(new RegisterModel(
            CreateMockUserManager().Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            CreateInMemoryDb(),
            Mock.Of<INotificationService>()));

        var result = model.OnGet();

        Assert.IsType<PageResult>(result);
    }

    // NOTE: OnGet_AuthenticatedUser_RedirectsToWorkspaces is intentionally skipped
    // because SignInManager requires HttpContext to be set, which is complex to mock
    // for Razor Page models. Authenticated redirect behavior is covered by integration tests.

    // ==========================================
    // Path A — Member registration (Gmail)
    // ==========================================

    [Fact]
    public async Task OnPostAsync_NormalUser_GmailEmail_CreatesAccountAndSignsIn()
    {
        var db = CreateInMemoryDb();
        var userManager = CreateMockUserManager();
        var signInManager = CreateMockSignInManager();

        var model = WithPageContext(new RegisterModel(
            userManager.Object,
            signInManager.Object,
            BuildConfig(requireEmailConfirmation: false),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            Mock.Of<INotificationService>()));

        model.Input = new RegisterModel.InputModel
        {
            Email = "member@gmail.com",
            AccountType = RegistrationAccountType.NormalUser,
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        var result = await model.OnPostAsync();

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/Workspaces", redirect.Url);

        userManager.Verify(x => x.CreateAsync(
            It.Is<ApplicationUser>(u =>
                u.Email == "member@gmail.com" &&
                u.AccountStatus == AccountStatus.Approved &&
                !u.AwaitingPmWorkspaceApproval),
            "Password1"), Times.Once);

        signInManager.Verify(x => x.SignInAsync(
            It.Is<ApplicationUser>(u => u.Email == "member@gmail.com"),
            false, null), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_NormalUser_NonGmailEmail_ReturnsModelError()
    {
        var db = CreateInMemoryDb();
        var model = WithPageContext(new RegisterModel(
            CreateMockUserManager().Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            Mock.Of<INotificationService>()));

        model.Input = new RegisterModel.InputModel
        {
            Email = "member@yahoo.com",
            AccountType = RegistrationAccountType.NormalUser,
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e => e.ErrorMessage.Contains("gmail.com")));
    }

    [Fact]
    public async Task OnPostAsync_DisplayName_UsesFullNameWhenProvided()
    {
        var db = CreateInMemoryDb();
        var userManager = CreateMockUserManager();

        var model = WithPageContext(new RegisterModel(
            userManager.Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            Mock.Of<INotificationService>()));

        model.Input = new RegisterModel.InputModel
        {
            Email = "member@gmail.com",
            AccountType = RegistrationAccountType.NormalUser,
            FullName = "John Doe",
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        await model.OnPostAsync();

        userManager.Verify(x => x.CreateAsync(
            It.Is<ApplicationUser>(u => u.DisplayName == "John Doe"),
            "Password1"), Times.Once);
    }

    [Fact]
    public async Task OnPostAsync_DisplayName_UsesEmailPrefixWhenNotProvided()
    {
        var db = CreateInMemoryDb();
        var userManager = CreateMockUserManager();

        var model = WithPageContext(new RegisterModel(
            userManager.Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            Mock.Of<INotificationService>()));

        model.Input = new RegisterModel.InputModel
        {
            Email = "johndoe@gmail.com",
            AccountType = RegistrationAccountType.NormalUser,
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        await model.OnPostAsync();

        userManager.Verify(x => x.CreateAsync(
            It.Is<ApplicationUser>(u => u.DisplayName == "johndoe"),
            "Password1"), Times.Once);
    }

    // ==========================================
    // Path B — PM registration (awaiting admin)
    // ==========================================

    [Fact]
    public async Task OnPostAsync_PmRequest_CreatesAccountWithPendingApprovalAndRedirects()
    {
        var db = CreateInMemoryDb();
        db.Users.Add(new ApplicationUser { Id = "admin1", IsPlatformAdmin = true, Email = "admin@workflow.local", UserName = "admin@workflow.local" });
        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager();
        var signInManager = CreateMockSignInManager();

        var model = WithPageContext(new RegisterModel(
            userManager.Object,
            signInManager.Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            Mock.Of<INotificationService>()));

        model.Input = new RegisterModel.InputModel
        {
            Email = "pm@company.com",
            AccountType = RegistrationAccountType.RequestPmWorkspace,
            WorkspaceOrCompanyName = "My Company",
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        var result = await model.OnPostAsync();

        var redirect = Assert.IsType<RedirectToPageResult>(result);
        Assert.Equal("./RegisterPending", redirect.PageName);

        userManager.Verify(x => x.CreateAsync(
            It.Is<ApplicationUser>(u =>
                u.Email == "pm@company.com" &&
                u.AccountStatus == AccountStatus.PendingApproval &&
                u.AwaitingPmWorkspaceApproval == true &&
                u.PendingWorkspaceName == "My Company"),
            "Password1"), Times.Once);

        signInManager.Verify(x => x.SignInAsync(It.IsAny<ApplicationUser>(), It.IsAny<bool>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task OnPostAsync_PmRequest_NotifiesAdmins()
    {
        var db = CreateInMemoryDb();
        db.Users.Add(new ApplicationUser { Id = "admin1", IsPlatformAdmin = true, Email = "admin@workflow.local", UserName = "admin@workflow.local" });
        db.Users.Add(new ApplicationUser { Id = "admin2", IsPlatformAdmin = true, Email = "admin2@workflow.local", UserName = "admin2@workflow.local" });
        await db.SaveChangesAsync();

        var notifications = new Mock<INotificationService>();
        var model = WithPageContext(new RegisterModel(
            CreateMockUserManager().Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            notifications.Object));

        model.Input = new RegisterModel.InputModel
        {
            Email = "pm@company.com",
            AccountType = RegistrationAccountType.RequestPmWorkspace,
            WorkspaceOrCompanyName = "My Company",
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        await model.OnPostAsync();

        notifications.Verify(x => x.CreateAndPushAsync(
            It.IsAny<string>(),
            NotificationType.RegistrationPendingPm,
            It.Is<string>(m => m.Contains("pm@company.com") && m.Contains("My Company")),
            null, null, null, It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task OnPostAsync_PmRequest_WithoutWorkspaceName_ReturnsModelError()
    {
        var db = CreateInMemoryDb();
        var model = WithPageContext(new RegisterModel(
            CreateMockUserManager().Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            Mock.Of<INotificationService>()));

        model.Input = new RegisterModel.InputModel
        {
            Email = "pm@company.com",
            AccountType = RegistrationAccountType.RequestPmWorkspace,
            WorkspaceOrCompanyName = null,
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e => e.ErrorMessage.Contains("tên đơn vị")));
    }

    [Fact]
    public async Task OnPostAsync_PmRequest_NonGmailEmail_IsAccepted()
    {
        var db = CreateInMemoryDb();
        db.Users.Add(new ApplicationUser { Id = "admin1", IsPlatformAdmin = true, Email = "admin@workflow.local", UserName = "admin@workflow.local" });
        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager();
        var model = WithPageContext(new RegisterModel(
            userManager.Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            Mock.Of<INotificationService>()));

        model.Input = new RegisterModel.InputModel
        {
            Email = "pm@company.com",
            AccountType = RegistrationAccountType.RequestPmWorkspace,
            WorkspaceOrCompanyName = "Company XYZ",
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<RedirectToPageResult>(result);
        Assert.DoesNotContain(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e => e.ErrorMessage.Contains("gmail")));
    }

    // ==========================================
    // Alternative sequences / error cases
    // ==========================================

    [Fact]
    public async Task OnPostAsync_EmailAlreadyExists_ReturnsModelError()
    {
        var db = CreateInMemoryDb();
        var existing = new ApplicationUser
        {
            Id = "existing-id",
            UserName = "existing@gmail.com",
            Email = "existing@gmail.com"
        };
        var userManager = CreateMockUserManager(existingUser: existing);

        var model = WithPageContext(new RegisterModel(
            userManager.Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            Mock.Of<INotificationService>()));

        model.Input = new RegisterModel.InputModel
        {
            Email = "existing@gmail.com",
            AccountType = RegistrationAccountType.NormalUser,
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e => e.ErrorMessage.Contains("exists", StringComparison.OrdinalIgnoreCase)));

        userManager.Verify(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task OnPostAsync_PasswordValidationFailsByIdentity_ReturnsModelError()
    {
        var db = CreateInMemoryDb();
        var userManager = CreateMockUserManager(
            createResult: IdentityResult.Failed(
                new IdentityError { Description = "Passwords must have at least one uppercase letter." }));

        var model = WithPageContext(new RegisterModel(
            userManager.Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            Mock.Of<INotificationService>()));

        model.Input = new RegisterModel.InputModel
        {
            Email = "test@gmail.com",
            AccountType = RegistrationAccountType.NormalUser,
            Password = "weak",
            ConfirmPassword = "weak"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e => e.ErrorMessage.Contains("uppercase", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task OnPostAsync_PmRequest_ExceptionDuringNotification_ReturnsErrorPage()
    {
        var db = CreateInMemoryDb();
        db.Users.Add(new ApplicationUser { Id = "admin1", IsPlatformAdmin = true, Email = "admin@workflow.local", UserName = "admin@workflow.local" });
        await db.SaveChangesAsync();

        var userManager = CreateMockUserManager();
        var notifications = new Mock<INotificationService>();
        notifications.Setup(x => x.CreateAndPushAsync(
                It.IsAny<string>(),
                It.IsAny<NotificationType>(),
                It.IsAny<string>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<Guid?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Email service down"));


        var model = WithPageContext(new RegisterModel(
            userManager.Object,
            CreateMockSignInManager().Object,
            BuildConfig(),
            Mock.Of<ILogger<RegisterModel>>(),
            db,
            notifications.Object));

        model.Input = new RegisterModel.InputModel
        {
            Email = "pm@company.com",
            AccountType = RegistrationAccountType.RequestPmWorkspace,
            WorkspaceOrCompanyName = "My Company",
            Password = "Password1",
            ConfirmPassword = "Password1"
        };

        var result = await model.OnPostAsync();

        Assert.IsType<PageResult>(result);
        Assert.Contains(model.ModelState.Keys,
            k => model.ModelState[k].Errors.Any(e => e.ErrorMessage.Contains("thất bại")));
    }

    // Helper: SignInManager subclass that allows setting Context via constructor
    private class TestableSignInManager : SignInManager<ApplicationUser>
    {
        public TestableSignInManager(UserManager<ApplicationUser> userManager, HttpContext context)
            : base(
                  userManager,
                  new HttpContextAccessor { HttpContext = context },
                  Mock.Of<IUserClaimsPrincipalFactory<ApplicationUser>>(),
                  new Mock<Microsoft.Extensions.Options.IOptions<IdentityOptions>>().Object,
                  Mock.Of<ILogger<SignInManager<ApplicationUser>>>(),
                  Mock.Of<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
                  Mock.Of<Microsoft.AspNetCore.Identity.IUserConfirmation<ApplicationUser>>())
        {
        }
    }
}
