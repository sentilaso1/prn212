using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using WorkFlowPro.Auth;
using WorkFlowPro.Services;

namespace WorkFlowPro.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IWorkspaceOnboardingService _workspaceOnboarding;
    private readonly IConfiguration _config;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IWorkspaceOnboardingService workspaceOnboarding,
        IConfiguration config,
        ILogger<RegisterModel> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _workspaceOnboarding = workspaceOnboarding;
        _config = config;
        _logger = logger;
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

