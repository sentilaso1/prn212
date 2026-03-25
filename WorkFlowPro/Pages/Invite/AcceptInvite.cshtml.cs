using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Invite;

[AllowAnonymous]
public sealed class AcceptInviteModel : PageModel
{
    private readonly IInvitationService _invitationService;

    public AcceptInviteModel(IInvitationService invitationService)
    {
        _invitationService = invitationService;
    }

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery] string? token,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            ErrorMessage = "Token không hợp lệ.";
            return Page();
        }

        var result = await _invitationService.AcceptInviteAsync(
            token,
            cancellationToken);

        if (!result.Success || result.WorkspaceId is null)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể chấp nhận lời mời.";
            return Page();
        }

        return LocalRedirect($"/Projects?workspaceId={result.WorkspaceId}");
    }
}

