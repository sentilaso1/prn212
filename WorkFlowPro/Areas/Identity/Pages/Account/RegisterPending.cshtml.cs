using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WorkFlowPro.Areas.Identity.Pages.Account;

[AllowAnonymous]
public class RegisterPendingModel : PageModel
{
    public void OnGet()
    {
    }
}
