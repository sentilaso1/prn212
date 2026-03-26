using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Invite;

[Authorize(Policy = "IsPM")]
public sealed class InviteModel : PageModel
{
    private readonly ICurrentWorkspaceService _currentWorkspaceService;
    private readonly IInvitationService _invitationService;

    public InviteModel(
        ICurrentWorkspaceService currentWorkspaceService,
        IInvitationService invitationService)
    {
        _currentWorkspaceService = currentWorkspaceService;
        _invitationService = invitationService;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [TempData]
    public string? InviteSuccessMessage { get; set; }

    public bool ShowSuccessToast => !string.IsNullOrWhiteSpace(InviteSuccessMessage);

    public Guid? CurrentWorkspaceId =>
        _currentWorkspaceService.CurrentWorkspaceId;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return Page();

        var workspaceId = _currentWorkspaceService.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        var result = await _invitationService.InviteMembersAsync(
            workspaceId.Value,
            Input.EmailsRaw,
            Input.Role,
            Input.SubRole,
            cancellationToken);

        if (!result.Success)
        {
            foreach (var err in result.Errors)
                ModelState.AddModelError(string.Empty, err);
            return Page();
        }

        InviteSuccessMessage = "Đã gửi lời mời thành công";
        return RedirectToPage();
    }

    public sealed class InputModel
    {
        [Required]
        [StringLength(2000, MinimumLength = 3)]
        public string EmailsRaw { get; set; } = default!;

        [Required]
        public WorkspaceMemberRole Role { get; set; } = WorkspaceMemberRole.Member;

        [Required(ErrorMessage = "SubRole là bắt buộc.")]
        [StringLength(100)]
        public string SubRole { get; set; } = default!;
    }
}

