using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Pages;

[Authorize(Policy = "CanManageWorkspaceRoles")]
public sealed class RolesModel : PageModel
{
    private readonly IRoleManagementService _roles;
    private readonly ICurrentWorkspaceService _currentWorkspace;
    private readonly IPlatformAdminService _platformAdmin;

    public RolesModel(
        IRoleManagementService roles,
        ICurrentWorkspaceService currentWorkspace,
        IPlatformAdminService platformAdmin)
    {
        _roles = roles;
        _currentWorkspace = currentWorkspace;
        _platformAdmin = platformAdmin;
    }

    [TempData]
    public string? ToastMessage { get; set; }

    public bool ShowToast => !string.IsNullOrWhiteSpace(ToastMessage);

    public string? ErrorMessage { get; private set; }

    public Guid? WorkspaceId { get; private set; }

    public IReadOnlyList<WorkspaceMemberRoleRowVm> Members { get; private set; } =
        Array.Empty<WorkspaceMemberRoleRowVm>();

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Chọn workspace để quản lý role.";
            return Page();
        }

        WorkspaceId = workspaceId;
        Members = await _roles.GetWorkspaceMembersAsync(workspaceId.Value, actorUserId, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostChangeRoleAsync(
        [Bind(Prefix = "ChangeRoleInput")] ChangeRolePostInput input,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        TryValidateModel(input, "ChangeRoleInput");
        if (!ModelState.IsValid)
        {
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        var result = await _roles.ChangeRoleAsync(
            workspaceId.Value,
            actorUserId,
            input.TargetUserId,
            input.NewRole,
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không đổi được role.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        ToastMessage = "Đã cập nhật workspace role.";
        return RedirectToPage("/Roles");
    }

    public async Task<IActionResult> OnPostChangeSubRoleAsync(
        [Bind(Prefix = "ChangeSubRoleInput")] ChangeSubRolePostInput input,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        TryValidateModel(input, "ChangeSubRoleInput");
        if (!ModelState.IsValid)
        {
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        var result = await _roles.ChangeSubRoleAsync(
            workspaceId.Value,
            actorUserId,
            input.TargetUserId,
            input.SubRole,
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không đổi được SubRole.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        ToastMessage = "Đã cập nhật SubRole.";
        return RedirectToPage("/Roles");
    }

    public async Task<IActionResult> OnPostRequestPromoteToPmAsync(
        [FromForm] string targetUserId,
        [FromForm] string? reason,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        var result = await _platformAdmin.SubmitPromoteToPmRequestAsync(
            actorUserId,
            workspaceId.Value,
            targetUserId,
            reason,
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không gửi được yêu cầu.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        ToastMessage = "Đã gửi yêu cầu nâng PM — chờ Admin duyệt.";
        return RedirectToPage("/Roles");
    }

    public async Task<IActionResult> OnPostRequestDemotePmAsync(
        [FromForm] string targetUserId,
        [FromForm] string? reason,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        var result = await _platformAdmin.SubmitDemotePmRequestAsync(
            actorUserId,
            workspaceId.Value,
            targetUserId,
            reason,
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không gửi được yêu cầu.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        ToastMessage = "Đã gửi yêu cầu hạ PM — chờ Admin duyệt.";
        return RedirectToPage("/Roles");
    }

    private async Task ReloadAsync(Guid workspaceId, string actorUserId, CancellationToken cancellationToken)
    {
        WorkspaceId = workspaceId;
        Members = await _roles.GetWorkspaceMembersAsync(workspaceId, actorUserId, cancellationToken);
    }

    public sealed class ChangeRolePostInput
    {
        [Required]
        public string TargetUserId { get; set; } = default!;

        [Required]
        public WorkspaceMemberRole NewRole { get; set; }
    }

    public sealed class ChangeSubRolePostInput
    {
        [Required]
        public string TargetUserId { get; set; } = default!;

        [StringLength(100)]
        public string? SubRole { get; set; }
    }
}
