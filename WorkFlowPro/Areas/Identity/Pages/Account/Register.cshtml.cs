using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Services;
using WorkFlowPro.Auth;

namespace WorkFlowPro.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IWorkspaceOnboardingService _workspaceOnboarding;
    private readonly IConfiguration _config;
    private readonly ILogger<RegisterModel> _logger;
    private readonly WorkFlowProDbContext _db;
    private readonly INotificationService _notifications;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IWorkspaceOnboardingService workspaceOnboarding,
        IConfiguration config,
        ILogger<RegisterModel> logger,
        WorkFlowProDbContext db,
        INotificationService notifications)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _workspaceOnboarding = workspaceOnboarding;
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

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var email = Input.Email.Trim();

        var existing = await _userManager.FindByEmailAsync(email);
        if (existing is not null)
        {
            ModelState.AddModelError(nameof(Input.Email), "Email already exists.");
            return Page();
        }

        var fullName = string.IsNullOrWhiteSpace(Input.FullName)
            ? email.Split('@')[0]
            : Input.FullName.Trim();

        var workspaceName = $"Workspace của {fullName}";

        var requireEmailConfirmation = _config.GetValue<bool>("Auth:RequireEmailConfirmation");

        // UC-01 yêu cầu auto SignIn sau đăng ký.
        // Identity vẫn có option email-confirm, nhưng trong scope này ta ưu tiên UX sign-in ngay.
        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = fullName,
            IsPlatformAdmin = false,
            EmailConfirmed = true
        };

        var createResult = await _userManager.CreateAsync(user, Input.Password);
        if (!createResult.Succeeded)
        {
            foreach (var error in createResult.Errors)
            {
                ModelState.AddModelError(string.Empty, error.Description);
            }

            return Page();
        }

        if (requireEmailConfirmation)
        {
            // TODO: Gửi email xác nhận (SendGrid/SMTP) + route ConfirmEmail.
            // Hiện tại app cấu hình Auth:RequireEmailConfirmation=false theo appsettings.Development.
            _logger.LogInformation("Email confirmation is enabled, but email sending is not implemented yet.");
        }

        try
        {
            // UC-03 extend: nếu email đang có lời mời chưa dùng, tạo notification cho user vừa đăng ký.
            var emailNormalized = email.ToLowerInvariant();
            var now = DateTime.UtcNow;

            var activeInvites = await _db.WorkspaceInviteTokens
                .AsNoTracking()
                .Where(t => t.Email == emailNormalized &&
                            t.UsedAtUtc == null &&
                            t.ExpiresAtUtc > now &&
                            t.AcceptUrl != null)
                .Join(
                    _db.Workspaces.AsNoTracking(),
                    t => t.WorkspaceId,
                    w => w.Id,
                    (t, w) => new { t.AcceptUrl, t.Role, t.SubRole, WorkspaceName = w.Name })
                .ToListAsync(HttpContext.RequestAborted);

            foreach (var inv in activeInvites)
            {
                // Tránh tạo trùng nếu user đăng ký và hệ thống đã tạo trước đó.
                var alreadyNotified = await _db.UserNotifications
                    .AsNoTracking()
                    .AnyAsync(n =>
                        n.UserId == user.Id &&
                        n.Type == NotificationType.WorkspaceInvite &&
                        n.RedirectUrl == inv.AcceptUrl,
                        HttpContext.RequestAborted);

                if (alreadyNotified)
                    continue;

                var roleLabel = inv.Role == WorkspaceMemberRole.PM ? "PM" : "Member";
                var subRoleSuffix = string.IsNullOrWhiteSpace(inv.SubRole)
                    ? string.Empty
                    : $" (SubRole: {inv.SubRole})";

                var notifMessage =
                    $"Bạn được mời vào workspace \"{inv.WorkspaceName}\". Vai trò: {roleLabel}.{subRoleSuffix}";

                await _notifications.CreateAndPushAsync(
                    user.Id,
                    NotificationType.WorkspaceInvite,
                    notifMessage,
                    workspaceId: null,
                    redirectUrl: inv.AcceptUrl,
                    cancellationToken: HttpContext.RequestAborted);
            }

            var workspace = await _workspaceOnboarding.CreateWorkspaceAndBootstrapUserAsync(
                userId: user.Id,
                workspaceName: workspaceName,
                cancellationToken: HttpContext.RequestAborted);

            await _signInManager.SignInAsync(user, isPersistent: false);
            return LocalRedirect($"/Workspaces?workspaceId={workspace.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bootstrap workspace for newly registered user.");
            ModelState.AddModelError(string.Empty, "Tạo Workspace tự động thất bại. Vui lòng thử lại.");
            return Page();
        }
    }
}

