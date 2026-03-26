using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WorkFlowPro.Pages;

/// <summary>
/// UC-01: Khách vào trang chủ → Đăng ký / Tạo Workspace. User đã đăng nhập → vào workspace (không xem landing).
/// </summary>
[AllowAnonymous]
public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Workspaces/Index");

        return Page();
    }
}
