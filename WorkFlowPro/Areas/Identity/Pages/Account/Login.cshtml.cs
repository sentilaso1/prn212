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

        if (user.AccountStatus != AccountStatus.Approved)
        {
            if (user.AccountStatus == AccountStatus.PendingApproval && user.AwaitingPmWorkspaceApproval)
            {
                ModelState.AddModelError(string.Empty,
                    "Tài khoản PM đang chờ Admin duyệt. Bạn chưa thể đăng nhập cho đến khi được chấp nhận.");
            }
            else if (user.AccountStatus == AccountStatus.Rejected)
            {
                ModelState.AddModelError(string.Empty, "Tài khoản đã bị từ chối.");
            }
            else
            {
                ModelState.AddModelError(string.Empty, "Tài khoản chưa được kích hoạt.");
            }

            return Page();
        }

        // CheckPasswordSignInAsync xử lý lockout theo Identity options.
        var signInResult = await _signInManager.CheckPasswordSignInAsync(
            user,
            Input.Password,
            lockoutOnFailure: true);

        if (signInResult.Succeeded)
        {
            // UC-15/UC-02: workspace có thể rỗng (user thường chờ lời mời vào đơn vị).
            var workspaceIds = await _db.WorkspaceMembers
                .Where(m => m.UserId == user.Id)
                .GroupBy(m => m.WorkspaceId)
                .OrderBy(g => g.Min(x => x.JoinedAtUtc))
                .Select(g => g.Key)
                .ToListAsync(HttpContext.RequestAborted);

            var principal = await _signInManager.CreateUserPrincipalAsync(user);

            if (principal.Identity is ClaimsIdentity identity)
            {
                if (user.IsPlatformAdmin && !principal.HasClaim("platform_role", "admin"))
                    identity.AddClaim(new Claim("platform_role", "admin"));

                if (workspaceIds.Count > 0)
                {
                    var activeWorkspaceId = workspaceIds[0];
                    var workspaceIdValue = activeWorkspaceId.ToString("D");

                    if (!principal.HasClaim("CurrentWorkspaceId", workspaceIdValue))
                        identity.AddClaim(new Claim("CurrentWorkspaceId", workspaceIdValue));

                    if (!principal.HasClaim("workspace_id", workspaceIdValue))
                        identity.AddClaim(new Claim("workspace_id", workspaceIdValue));
                }
            }

            await HttpContext.SignInAsync(
                IdentityConstants.ApplicationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = Input.RememberMe
                });

            if (workspaceIds.Count == 0)
                return LocalRedirect("/Workspaces");

            var firstWs = workspaceIds[0];
            return workspaceIds.Count > 1
                ? LocalRedirect("/Workspaces")
                : LocalRedirect($"/Workspaces?workspaceId={firstWs}");
        }

        if (signInResult.IsLockedOut)
        {
            // Hiển thị thời gian còn lại đến khi mở khóa (UC-02).
            var refreshedUser = await _userManager.FindByEmailAsync(email);
            var lockoutEndUtc = refreshedUser?.LockoutEnd;

            string message;
            if (lockoutEndUtc.HasValue)
            {
                var remaining = lockoutEndUtc.Value - DateTime.UtcNow;
                if (remaining > TimeSpan.Zero)
                {
                    // Ví dụ: "trong 00:12:34"
                    message = $"Tài khoản bị khóa do nhập sai quá nhiều lần. Vui lòng thử lại sau {remaining.ToString(@"hh\:mm\:ss")}.";
                }
                else
                {
                    message = "Tài khoản hiện đang bị khóa do nhập sai quá nhiều lần. Vui lòng thử lại sau ít phút.";
                }
            }
            else
            {
                message = "Tài khoản bị khóa do nhập sai quá nhiều lần. Vui lòng thử lại sau ít phút.";
            }

            ModelState.AddModelError(string.Empty, message);
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

