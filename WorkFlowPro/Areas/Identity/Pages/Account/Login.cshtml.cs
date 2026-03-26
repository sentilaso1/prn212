using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;

namespace WorkFlowPro.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class LoginModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly WorkFlowProDbContext _db;

    public LoginModel(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        WorkFlowProDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = default!;

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = default!;

        public bool RememberMe { get; set; }
    }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var email = Input.Email.Trim();
        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return Page();
        }

        // CheckPasswordSignInAsync xử lý lockout theo Identity options.
        var signInResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            Input.Password,
            lockoutOnFailure: true);

        if (signInResult.Succeeded)
        {
            // UC-15/UC-02: xác định workspace(s) của user.
            var workspaceIds = await _db.WorkspaceMembers
                .Where(m => m.UserId == user.Id)
                .GroupBy(m => m.WorkspaceId)
                .OrderBy(g => g.Min(x => x.JoinedAtUtc))
                .Select(g => g.Key)
                .ToListAsync(HttpContext.RequestAborted);

            if (workspaceIds.Count == 0)
            {
                ModelState.AddModelError(string.Empty, "User has no workspace.");
                return Page();
            }

            var activeWorkspaceId = workspaceIds[0];

            var principal = await _signInManager.CreateUserPrincipalAsync(user);
            var workspaceIdValue = activeWorkspaceId.ToString("D");

            // Requirement: add claim CurrentWorkspaceId after login.
            if (principal.Identity is ClaimsIdentity identity)
            {
                if (!principal.HasClaim("CurrentWorkspaceId", workspaceIdValue))
                    identity.AddClaim(new Claim("CurrentWorkspaceId", workspaceIdValue));

                // Tương thích JWT claim hiện có.
                if (!principal.HasClaim("workspace_id", workspaceIdValue))
                    identity.AddClaim(new Claim("workspace_id", workspaceIdValue));
            }

            await HttpContext.SignInAsync(
                IdentityConstants.ApplicationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = Input.RememberMe
                });

            // Redirect:
            // - Nhiều workspace -> Workspace Switching (UC-15) => /Workspaces
            // - 1 workspace -> Dashboard/Kanban => /Workspaces?workspaceId=...
            return workspaceIds.Count > 1
                ? LocalRedirect("/Workspaces")
                : LocalRedirect($"/Workspaces?workspaceId={activeWorkspaceId}");
        }

        if (signInResult.IsLockedOut)
        {
            // UC-02: Hiển thị rõ thời gian khóa theo cấu hình.
            // Identity mặc định đang khóa 5 phút, nhưng để hiển thị đúng thực tế ta vẫn tính remaining khi có dữ liệu.
            var refreshedUser = await _userManager.FindByEmailAsync(email);
            var lockoutEndUtc = refreshedUser?.LockoutEnd;

            var remaining = lockoutEndUtc.HasValue
                ? lockoutEndUtc.Value - DateTime.UtcNow
                : TimeSpan.FromMinutes(5);

            if (remaining > TimeSpan.Zero)
                ModelState.AddModelError(string.Empty,
                    $"Tài khoản bị khóa 5 phút do nhập sai quá nhiều lần. Vui lòng thử lại sau {remaining.ToString(@"mm\:ss")}.");
            else
                ModelState.AddModelError(string.Empty,
                    "Tài khoản bị khóa 5 phút do nhập sai quá nhiều lần. Vui lòng thử lại.");

            return Page();
        }

        if (signInResult.RequiresTwoFactor)
        {
            // Hiện chưa implement trang 2FA trong scope project.
            ModelState.AddModelError(string.Empty, "Two-factor authentication is required but not implemented yet.");
            return Page();
        }

        if (signInResult.IsNotAllowed)
        {
            ModelState.AddModelError(string.Empty, "Sign-in is not allowed for this account.");
            return Page();
        }

        ModelState.AddModelError(string.Empty, "Invalid login attempt.");
        return Page();
    }
}

