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

[Authorize]
public sealed class ProfileModel : PageModel
{
    private readonly IMemberProfileService _memberProfile;
    private readonly ICurrentWorkspaceService _currentWorkspace;
    private readonly WorkFlowProDbContext _db;

    public ProfileModel(
        IMemberProfileService memberProfile,
        ICurrentWorkspaceService currentWorkspace,
        WorkFlowProDbContext db)
    {
        _memberProfile = memberProfile;
        _currentWorkspace = currentWorkspace;
        _db = db;
    }

    [TempData]
    public string? ToastMessage { get; set; }

    public bool ShowToast => !string.IsNullOrWhiteSpace(ToastMessage);

    public MemberProfilePageVm? Profile { get; private set; }

    public string? ErrorMessage { get; private set; }

    public Guid? WorkspaceId { get; private set; }

    /// <summary>Hiển thị form GET; POST dùng parameter binding để tránh xung đột validation giữa 2 form.</summary>
    public ProfileEditInput Input { get; set; } = new();

    public PmMemberEditInput PmInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync([FromQuery] string? userId, CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Chỉ xem profile khi đã chọn workspace.";
            return Page();
        }

        WorkspaceId = workspaceId;

        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var targetUserId = string.IsNullOrWhiteSpace(userId)
            ? actorUserId
            : userId.Trim();

        var targetInWorkspace = await _db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m => m.WorkspaceId == workspaceId.Value && m.UserId == targetUserId, cancellationToken);

        if (!targetInWorkspace)
        {
            var standalone = await TryBuildStandalonePlatformAdminProfileAsync(actorUserId, targetUserId, cancellationToken);
            if (standalone is null)
                return NotFound();

            Profile = standalone;
            Input.TargetUserId = targetUserId;
            Input.FullName = Profile.FullName;
            PmInput.TargetUserId = targetUserId;
            PmInput.Level = Profile.Level;
            PmInput.SubRole = Profile.SubRole ?? string.Empty;
            return Page();
        }

        var isPm = await _db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m =>
                    m.WorkspaceId == workspaceId.Value &&
                    m.UserId == actorUserId &&
                    m.Role == WorkspaceMemberRole.PM,
                cancellationToken);

        if (actorUserId != targetUserId && !isPm)
            return Forbid();

        var vm = await _memberProfile.GetProfilePageAsync(
            workspaceId.Value,
            actorUserId,
            targetUserId,
            cancellationToken);

        if (vm is null)
            return NotFound();

        Profile = vm;
        Input.TargetUserId = vm.TargetUserId;
        Input.FullName = vm.FullName;
        PmInput.TargetUserId = vm.TargetUserId;
        PmInput.Level = vm.Level;
        PmInput.SubRole = vm.SubRole ?? string.Empty;

        return Page();
    }

    public async Task<IActionResult> OnPostUpdateProfileAsync(
        [Bind(Prefix = "Input")] ProfileEditInput input,
        CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        TryValidateModel(input, "Input");
        if (!ModelState.IsValid)
            return await ReloadWithErrorAsync(workspaceId.Value, actorUserId, input.TargetUserId, cancellationToken, profileInput: input);

        var result = await _memberProfile.UpdateProfileAsync(
            workspaceId.Value,
            actorUserId,
            input.TargetUserId,
            input.FullName,
            input.AvatarFile,
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Cập nhật thất bại.");
            return await ReloadWithErrorAsync(workspaceId.Value, actorUserId, input.TargetUserId, cancellationToken, profileInput: input);
        }

        ToastMessage = "Đã cập nhật hồ sơ.";
        return RedirectToPage("/Profile");
    }

    public async Task<IActionResult> OnPostUpdatePmAsync(
        [Bind(Prefix = "PmInput")] PmMemberEditInput pmInput,
        CancellationToken cancellationToken)
    {
        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
            return Challenge();

        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        TryValidateModel(pmInput, "PmInput");
        if (!ModelState.IsValid)
            return await ReloadWithErrorAsync(workspaceId.Value, actorUserId, pmInput.TargetUserId, cancellationToken, pmInput: pmInput);

        var mpRow = await _db.MemberProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == pmInput.TargetUserId, cancellationToken);
        var currentLevel = mpRow?.Level ?? MemberLevel.Junior;

        MemberProfileResult result;
        if (pmInput.Level == currentLevel)
        {
            result = await _memberProfile.UpdateMemberSubRoleOnlyAsync(
                workspaceId.Value,
                actorUserId,
                pmInput.TargetUserId,
                string.IsNullOrWhiteSpace(pmInput.SubRole) ? null : pmInput.SubRole.Trim(),
                cancellationToken);
            if (result.Success)
                ToastMessage = "Đã cập nhật SubRole.";
        }
        else
        {
            var j = pmInput.LevelJustification?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(j))
            {
                ModelState.AddModelError("PmInput.LevelJustification", "Cần lý do / căn cứ khi đề xuất đổi Level (UC-10).");
                return await ReloadWithErrorAsync(workspaceId.Value, actorUserId, pmInput.TargetUserId, cancellationToken, pmInput: pmInput);
            }

            result = await _memberProfile.SubmitLevelAdjustmentProposalAsync(
                workspaceId.Value,
                actorUserId,
                pmInput.TargetUserId,
                pmInput.Level,
                j,
                cancellationToken);
            if (result.Success)
                ToastMessage = "Đã gửi đề xuất đổi Level — chờ Admin duyệt (UC-13).";
        }

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Cập nhật thất bại.");
            return await ReloadWithErrorAsync(workspaceId.Value, actorUserId, pmInput.TargetUserId, cancellationToken, pmInput: pmInput);
        }

        return RedirectToPage("/Profile", new { userId = pmInput.TargetUserId });
    }

    /// <summary>UC-14: Platform admin không có membership trong workspace đang chọn vẫn xem/sửa profile của chính mình.</summary>
    private async Task<MemberProfilePageVm?> TryBuildStandalonePlatformAdminProfileAsync(
        string actorUserId,
        string targetUserId,
        CancellationToken cancellationToken)
    {
        if (actorUserId != targetUserId)
            return null;

        var isPlatformAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == actorUserId && u.IsPlatformAdmin, cancellationToken);
        if (!isPlatformAdmin)
            return null;

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);
        if (user is null)
            return null;

        var mp = await _db.MemberProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == targetUserId, cancellationToken);
        mp ??= new MemberProfile { UserId = targetUserId };

        return new MemberProfilePageVm
        {
            TargetUserId = targetUserId,
            Email = user.Email ?? user.UserName ?? targetUserId,
            FullName = user.DisplayName ?? user.Email ?? user.UserName ?? targetUserId,
            AvatarUrl = user.AvatarUrl,
            SubRole = null,
            WorkspaceRole = WorkspaceMemberRole.Member,
            Level = mp.Level,
            CompletionRate = mp.CompletionRate,
            AvgScore = mp.AvgScore,
            CurrentWorkload = mp.CurrentWorkload,
            IsSelf = true,
            IsPm = false,
            CanEditProfile = true,
            CanEditLevelOrSubRole = false,
            HasPendingLevelAdjustment = false,
            IsStandalonePlatformAdmin = true,
            TaskHistory = Array.Empty<ProfileTaskHistoryRowVm>()
        };
    }

    private async Task<IActionResult> ReloadWithErrorAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        CancellationToken cancellationToken,
        ProfileEditInput? profileInput = null,
        PmMemberEditInput? pmInput = null)
    {
        WorkspaceId = workspaceId;
        var vm = await _memberProfile.GetProfilePageAsync(workspaceId, actorUserId, targetUserId, cancellationToken);
        if (vm is null)
            vm = await TryBuildStandalonePlatformAdminProfileAsync(actorUserId, targetUserId, cancellationToken);
        if (vm is null)
            return NotFound();
        Profile = vm;

        if (profileInput is not null)
        {
            Input.TargetUserId = profileInput.TargetUserId;
            Input.FullName = profileInput.FullName;
            PmInput.TargetUserId = vm.TargetUserId;
            PmInput.Level = vm.Level;
            PmInput.SubRole = vm.SubRole ?? string.Empty;
        }
        else if (pmInput is not null)
        {
            Input.TargetUserId = vm.TargetUserId;
            Input.FullName = vm.FullName;
            PmInput.TargetUserId = pmInput.TargetUserId;
            PmInput.Level = pmInput.Level;
            PmInput.SubRole = pmInput.SubRole ?? string.Empty;
            PmInput.LevelJustification = pmInput.LevelJustification;
        }
        else
        {
            Input.TargetUserId = vm.TargetUserId;
            Input.FullName = vm.FullName;
            PmInput.TargetUserId = vm.TargetUserId;
            PmInput.Level = vm.Level;
            PmInput.SubRole = vm.SubRole ?? string.Empty;
        }

        return Page();
    }

    public sealed class ProfileEditInput
    {
        public string TargetUserId { get; set; } = default!;

        [Required(ErrorMessage = "Họ tên là bắt buộc.")]
        [StringLength(200, MinimumLength = 1)]
        public string FullName { get; set; } = default!;

        public IFormFile? AvatarFile { get; set; }
    }

    public sealed class PmMemberEditInput
    {
        public string TargetUserId { get; set; } = default!;

        [Required]
        public MemberLevel Level { get; set; } = MemberLevel.Junior;

        [StringLength(100)]
        public string? SubRole { get; set; }

        /// <summary>Bắt buộc khi đổi Level so với hiện tại.</summary>
        [StringLength(2000)]
        public string? LevelJustification { get; set; }
    }
}
