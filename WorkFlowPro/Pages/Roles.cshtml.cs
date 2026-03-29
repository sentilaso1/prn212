using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Pages;

[Authorize(Policy = "CanManageWorkspaceRoles")]
public sealed class RolesModel : PageModel
{
    private readonly WorkFlowProDbContext _db;
    private readonly IRoleManagementService _roles;
    private readonly ICurrentWorkspaceService _currentWorkspace;
    private readonly IPlatformAdminService _platformAdmin;

    public RolesModel(
        WorkFlowProDbContext db,
        IRoleManagementService roles,
        ICurrentWorkspaceService currentWorkspace,
        IPlatformAdminService platformAdmin)
    {
        _db = db;
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

    /// <summary>UC-03 Path C: PM xóa Member — lý do bắt buộc.</summary>
    public async Task<IActionResult> OnPostRemoveMemberAsync(
        [Bind(Prefix = "RemoveMemberInput")] RemoveMemberPostInput input,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        TryValidateModel(input, "RemoveMemberInput");
        if (!ModelState.IsValid)
        {
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        var result = await _roles.RemoveMemberFromWorkspaceAsync(
            workspaceId.Value,
            actorUserId,
            input.TargetUserId,
            input.Reason,
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không xóa được thành viên.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        ToastMessage = "Đã xóa thành viên khỏi đơn vị.";
        return RedirectToPage("/Roles");
    }

    /// <summary>PM: yêu cầu đổi PM↔Member — luôn qua Admin. SubRole chỉ qua ChangeSubRole.</summary>
    public async Task<IActionResult> OnPostRequestWorkspaceRoleChangeAsync(
        [FromForm] string targetUserId,
        [FromForm] WorkspaceMemberRole requestedRole,
        [FromForm] string? reason,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        var member = await _db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(
                m => m.WorkspaceId == workspaceId.Value && m.UserId == targetUserId,
                cancellationToken);

        if (member is null)
        {
            ModelState.AddModelError(string.Empty, "Không tìm thấy thành viên.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        if (member.Role == requestedRole)
        {
            ModelState.AddModelError(string.Empty,
                "Form này chỉ dùng khi cần đổi PM↔Member. Đổi SubRole (BA / DEV / Designer / QA) hãy dùng «Lưu SubRole» bên dưới — không cần Admin.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        var encodedReason = RoleRequestReasonEncoding.Encode(null, reason);

        AdminActionResult result;
        if (member.Role == WorkspaceMemberRole.Member && requestedRole == WorkspaceMemberRole.PM)
        {
            result = await _platformAdmin.SubmitPromoteToPmRequestAsync(
                actorUserId,
                workspaceId.Value,
                targetUserId,
                encodedReason,
                cancellationToken);
        }
        else if (member.Role == WorkspaceMemberRole.PM && requestedRole == WorkspaceMemberRole.Member)
        {
            result = await _platformAdmin.SubmitDemotePmRequestAsync(
                actorUserId,
                workspaceId.Value,
                targetUserId,
                encodedReason,
                cancellationToken);
        }
        else
        {
            ModelState.AddModelError(string.Empty, "Yêu cầu đổi role không hợp lệ.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Không gửi được yêu cầu.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        ToastMessage = "Đã gửi yêu cầu thay đổi role — chờ Admin duyệt.";
        return RedirectToPage("/Roles");
    }

    public async Task<IActionResult> OnPostRequestRemovePmAsync(
        [FromForm] string targetUserId,
        [FromForm] string reason,
        CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        if (string.IsNullOrWhiteSpace(reason))
        {
            ModelState.AddModelError(string.Empty, "Lý do là bắt buộc.");
            await ReloadAsync(workspaceId.Value, actorUserId, cancellationToken);
            return Page();
        }

        var result = await _platformAdmin.SubmitRemovePmFromWorkspaceRequestAsync(
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

        ToastMessage = "Đã gửi yêu cầu xóa PM — chờ Admin duyệt.";
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

    public sealed class RemoveMemberPostInput
    {
        [Required]
        public string TargetUserId { get; set; } = default!;

        [Required(ErrorMessage = "Lý do xóa thành viên là bắt buộc.")]
        [StringLength(2000)]
        public string Reason { get; set; } = default!;
    }
}
