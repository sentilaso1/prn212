using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Data;
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

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public string? ErrorMessage { get; private set; }
    public string? SuccessMessage { get; private set; }
    public InviteInfoResult? InviteInfo { get; private set; }
    public bool ShowForm { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            ErrorMessage = "Token không hợp lệ.";
            return;
        }

        InviteInfo = await _invitationService.GetInviteInfoAsync(Token, cancellationToken);

        if (InviteInfo is null)
        {
            ErrorMessage = "Lời mời không tồn tại hoặc token sai.";
            return;
        }

        if (InviteInfo.Status != InviteStatus.Pending)
        {
            ErrorMessage = InviteInfo.Status == InviteStatus.Accepted
                ? "Lời mời này đã được chấp nhận."
                : "Lời mời này đã bị từ chối.";
            return;
        }

        ShowForm = true;
    }

    public async Task<IActionResult> OnPostAcceptAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            ErrorMessage = "Token không hợp lệ.";
            return Page();
        }

        var result = await _invitationService.AcceptInviteAsync(Token, cancellationToken);

        if (!result.Success || result.WorkspaceId is null)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể chấp nhận lời mời.";
            InviteInfo = await _invitationService.GetInviteInfoAsync(Token, cancellationToken);
            return Page();
        }

        return LocalRedirect($"/Workspaces?workspaceId={result.WorkspaceId}");
    }

    public async Task<IActionResult> OnPostRejectAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            ErrorMessage = "Token không hợp lệ.";
            return Page();
        }

        var result = await _invitationService.RejectInviteAsync(Token, cancellationToken);

        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage ?? "Không thể từ chối lời mời.";
            InviteInfo = await _invitationService.GetInviteInfoAsync(Token, cancellationToken);
            return Page();
        }

        SuccessMessage = "Bạn đã từ chối lời mời thành công.";
        return Page();
    }
}
