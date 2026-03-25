using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Auth;

namespace WorkFlowPro.Areas.Identity.Pages.Account;

[AllowAnonymous]
public sealed class ResetPasswordModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly WorkFlowProDbContext _db;

    public ResetPasswordModel(
        UserManager<ApplicationUser> userManager,
        WorkFlowProDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string UserId { get; set; } = default!;

    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = default!;

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? ErrorMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        if (string.IsNullOrWhiteSpace(UserId) || string.IsNullOrWhiteSpace(Token))
        {
            ErrorMessage = "Invalid reset link.";
            return Page();
        }

        var tokenHash = ComputeSha256Hex(Token);

        var reset = await _db.PasswordResetTokens
            .FirstOrDefaultAsync(t =>
                t.UserId == UserId &&
                t.TokenHash == tokenHash &&
                t.UsedAtUtc == null &&
                t.ExpiresAtUtc >= DateTime.UtcNow,
                cancellationToken);

        if (reset is null)
        {
            ErrorMessage = "Link is invalid or expired.";
            return Page();
        }

        var user = await _userManager.FindByIdAsync(UserId);
        if (user is null)
        {
            ErrorMessage = "User not found.";
            return Page();
        }

        // Update password hash directly (Token flow validated by our DB record).
        var hasher = new PasswordHasher<ApplicationUser>();
        user.PasswordHash = hasher.HashPassword(user, Input.NewPassword);
        await _userManager.UpdateAsync(user);

        reset.UsedAtUtc = DateTime.UtcNow;
        _db.PasswordResetTokens.Update(reset);
        await _db.SaveChangesAsync(cancellationToken);

        // Redirect to login.
        return LocalRedirect("/Identity/Account/Login?reset=1");
    }

    public sealed class InputModel
    {
        [Required]
        [DataType(DataType.Password)]
        [StringLength(100, MinimumLength = 8)]
        [RegularExpression(@"^(?=.*\d)(?=.*[A-Z]).+$", ErrorMessage = "Password must contain a digit and an uppercase letter.")]
        public string NewPassword { get; set; } = default!;

        [Required]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "Password confirmation does not match.")]
        public string ConfirmPassword { get; set; } = default!;
    }

    private static string ComputeSha256Hex(string token)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(token);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToHexString(hash);
    }
}

