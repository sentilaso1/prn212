using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;

namespace WorkFlowPro.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class ForgotPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly WorkFlowProDbContext _db;
    private readonly ILogger<ForgotPasswordModel> _logger;
    private readonly IWebHostEnvironment _env;

    public ForgotPasswordModel(
        UserManager<ApplicationUser> userManager,
        WorkFlowProDbContext db,
        ILogger<ForgotPasswordModel> logger,
        IWebHostEnvironment env)
    {
        _userManager = userManager;
        _db = db;
        _logger = logger;
        _env = env;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? StatusMessage { get; private set; }
    public string? DevResetLink { get; private set; }

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = default!;
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        var email = Input.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);

        // SRS: nếu email hợp lệ -> gửi link. UX: luôn trả lời chung (không lộ user tồn tại).
        StatusMessage = "Nếu email hợp lệ, bạn sẽ nhận được hướng dẫn đặt lại mật khẩu.";

        if (user is null)
        {
            return Page();
        }

        // Generate token (raw token sent to user), store only hash.
        var token = GenerateRandomToken();
        var tokenHash = ComputeSha256Hex(token);

        var reset = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = tokenHash,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(1),
            UsedAtUtc = null
        };

        _db.PasswordResetTokens.Add(reset);
        await _db.SaveChangesAsync(cancellationToken);

        // Email sending is optional (no IEmailSender in this project).
        var resetPath = $"/Identity/Account/ResetPassword?userId={Uri.EscapeDataString(user.Id)}&token={Uri.EscapeDataString(token)}";
        var absolute = resetPath;

        _logger.LogInformation("Password reset link for {Email}: {Link}", email, absolute);

        if (_env.IsDevelopment())
        {
            DevResetLink = absolute;
        }

        return Page();
    }

    private static string GenerateRandomToken(int bytes = 32)
    {
        var buffer = RandomNumberGenerator.GetBytes(bytes);
        return Convert.ToBase64String(buffer);
    }

    private static string ComputeSha256Hex(string token)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}

