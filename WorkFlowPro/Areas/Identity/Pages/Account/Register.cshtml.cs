using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IConfiguration _config;
    private readonly ILogger<RegisterModel> _logger;
    private readonly WorkFlowProDbContext _db;
    private readonly INotificationService _notifications;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IConfiguration config,
        ILogger<RegisterModel> logger,
        WorkFlowProDbContext db,
        INotificationService notifications)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
        _logger = logger;
        _db = db;
        _notifications = notifications;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = default!;

        /// <summary>0 = user thường (Gmail), 1 = PM — chờ Admin duyệt mới có đơn vị.</summary>
        [Required]
        public RegistrationAccountType AccountType { get; set; } = RegistrationAccountType.NormalUser;

        /// <summary>Tên đơn vị / công ty — bắt buộc khi đăng ký PM.</summary>
        [StringLength(200)]
        public string? WorkspaceOrCompanyName { get; set; }

        [StringLength(100)]
        public string? FullName { get; set; }

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = default!;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = default!;
    }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            TempData["RegisterBlockedMessage"] =
                "Bạn đang đăng nhập. Đăng xuất trước hoặc dùng cửa sổ ẩn danh để đăng ký tài khoản mới.";
            return RedirectToPage("/Workspaces/Index");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Input.AccountType == RegistrationAccountType.RequestPmWorkspace)
        {
            if (string.IsNullOrWhiteSpace(Input.WorkspaceOrCompanyName))
                ModelState.AddModelError(nameof(Input.WorkspaceOrCompanyName),
                    "Nhập tên đơn vị (sẽ tạo sau khi Admin duyệt).");
        }
        else
        {
            Input.WorkspaceOrCompanyName = null;
        }

        if (Input.AccountType == RegistrationAccountType.NormalUser &&
            !RegistrationEmailRules.IsGmailConsumerEmail(Input.Email))
        {
            ModelState.AddModelError(nameof(Input.Email),
                "Tài khoản thường chỉ chấp nhận email @gmail.com.");
        }

        if (!ModelState.IsValid)
            return Page();

        var email = Input.Email.Trim();

        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(Input.Email), "Email already exists.");
            return Page();
        }

        var displayName = string.IsNullOrWhiteSpace(Input.FullName)
            ? email.Split('@')[0]
            : Input.FullName.Trim();

        var requireEmailConfirmation = _config.GetValue<bool>("Auth:RequireEmailConfirmation");

        var isPmRequest = Input.AccountType == RegistrationAccountType.RequestPmWorkspace;
        var pendingName = isPmRequest ? Input.WorkspaceOrCompanyName?.Trim() : null;

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            IsPlatformAdmin = false,
            EmailConfirmed = !requireEmailConfirmation,
            AccountStatus = isPmRequest ? AccountStatus.PendingApproval : AccountStatus.Approved,
            AwaitingPmWorkspaceApproval = isPmRequest,
            PendingWorkspaceName = string.IsNullOrWhiteSpace(pendingName) ? null : pendingName
        };

        var createResult = await _userManager.CreateAsync(user, Input.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return Page();
        }

        if (requireEmailConfirmation)
        {
            _logger.LogInformation("Email confirmation enabled; SMTP chưa gắn.");
        }

        try
        {
            if (isPmRequest)
            {
                var adminIds = await _db.Users.AsNoTracking()
                    .Where(u => u.IsPlatformAdmin)
                    .Select(u => u.Id)
                    .ToListAsync(HttpContext.RequestAborted);

                foreach (var adminId in adminIds)
                {
                    await _notifications.CreateAndPushAsync(
                        adminId,
                        NotificationType.RegistrationPendingPm,
                        $"Đăng ký PM mới: {email} — đơn vị dự kiến: \"{pendingName ?? "—"}\".",
                        workspaceId: null,
                        redirectUrl: "/Admin",
                        cancellationToken: HttpContext.RequestAborted);
                }

                return RedirectToPage("./RegisterPending");
            }

            // User thường: không tạo đơn vị — đăng nhập ngay, vào trang chưa có đơn vị.
            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect("/Workspaces");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Register bootstrap failed for {Email}", email);
            ModelState.AddModelError(string.Empty, "Đăng ký thất bại. Vui lòng thử lại.");
            return Page();
        }
    }
}

public enum RegistrationAccountType
{
    NormalUser = 0,
    RequestPmWorkspace = 1
}
