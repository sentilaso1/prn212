using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
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

        // Strip ALL existing workspaceId from target to prevent accumulation,
        // then append exactly one workspaceId.
        var queryIndex = target.IndexOf('?');
        var path = queryIndex >= 0 ? target[..queryIndex] : target;
        var queryPart = queryIndex >= 0 ? target[(queryIndex + 1)..] : string.Empty;

        var cleanQuery = string.IsNullOrWhiteSpace(queryPart)
            ? new List<KeyValuePair<string, string?>>()
            : QueryHelpers.ParseQuery(queryPart)
                .Where(kvp => !string.Equals(kvp.Key, "workspaceId", StringComparison.OrdinalIgnoreCase))
                .SelectMany(kvp => kvp.Value.Select(v => new KeyValuePair<string, string?>(kvp.Key, v)))
                .ToList();

        cleanQuery.Add(new KeyValuePair<string, string?>("workspaceId", workspaceId.ToString("D")));
        var redirectUrl = path + QueryString.Create(cleanQuery);

        return LocalRedirect(redirectUrl);
    }
}
