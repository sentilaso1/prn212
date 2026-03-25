using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Workspaces;

[Authorize]
public sealed class SwitchModel : PageModel
{
    private readonly IUserWorkspaceService _userWorkspaceService;

    public SwitchModel(IUserWorkspaceService userWorkspaceService)
    {
        _userWorkspaceService = userWorkspaceService;
    }

    public async Task<IActionResult> OnGetAsync(Guid workspaceId, string? returnUrl)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return Challenge();

        var isMember = await _userWorkspaceService.IsUserMemberOfWorkspaceAsync(
            userId,
            workspaceId,
            HttpContext.RequestAborted);

        if (!isMember)
        {
            TempData["WorkspaceSwitchError"] =
                "Bạn đã không còn là thành viên của workspace này. Hệ thống sẽ chuyển bạn sang workspace khác.";

            var firstWorkspaceId = await _userWorkspaceService.GetFirstWorkspaceIdAsync(
                userId,
                HttpContext.RequestAborted);

            if (firstWorkspaceId is null)
                return LocalRedirect("/Workspaces");

            workspaceId = firstWorkspaceId.Value;
        }

        var target = string.IsNullOrWhiteSpace(returnUrl) || !Url.IsLocalUrl(returnUrl)
            ? "/Workspaces"
            : returnUrl;

        // Always store workspaceId in querystring so claims transformation cập nhật session/claims.
        var redirectUrl = QueryHelpers.AddQueryString(
            target,
            "workspaceId",
            workspaceId.ToString("D"));

        return LocalRedirect(redirectUrl);
    }
}

