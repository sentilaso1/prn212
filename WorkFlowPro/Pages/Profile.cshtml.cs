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
    private readonly ILevelAdjustmentService _levelAdjustment;
    private readonly ICurrentWorkspaceService _currentWorkspace;
    private readonly WorkFlowProDbContext _db;

    public ProfileModel(
        IMemberProfileService memberProfile,
        ILevelAdjustmentService levelAdjustment,
        ICurrentWorkspaceService currentWorkspace,
        WorkFlowProDbContext db)
    {
        _memberProfile = memberProfile;
        _levelAdjustment = levelAdjustment;
        _currentWorkspace = currentWorkspace;
        _db = db;
    }

    [TempData]
    public string? ToastMessage { get; set; }

    public bool ShowToast => !string.IsNullOrWhiteSpace(ToastMessage);

    public MemberProfilePageVm? Profile { get; private set; }

    public BasicProfileVm? BasicProfile { get; private set; }

    public string? ErrorMessage { get; private set; }

    public Guid? WorkspaceId { get; private set; }

    /// <summary>Hiển thị form GET; POST dùng parameter binding để tránh xung đột validation giữa 2 form.</summary>
    public ProfileEditInput Input { get; set; } = new();

    public PmMemberEditInput PmInput { get; set; } = new();

    public async Task<IActionResult> OnGetAsync([FromQuery] string? userId, CancellationToken cancellationToken)
    {
        var actorUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return Challenge();

        var targetUserId = string.IsNullOrWhiteSpace(userId)
            ? actorUserId
            : userId.Trim();

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        WorkspaceId = workspaceId;

        if (workspaceId is null)
        {
            if (targetUserId != actorUserId)
                return Forbid();

            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);

            if (user is null)
                return NotFound();

            BasicProfile = new BasicProfileVm
            {
                UserId = targetUserId,
                Email = user.Email ?? user.UserName ?? targetUserId,
                FullName = user.DisplayName ?? user.Email ?? user.UserName ?? targetUserId,
                AvatarUrl = user.AvatarUrl
            };

            return Page();
        }

        var actorInWorkspace = await _db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m => m.WorkspaceId == workspaceId.Value && m.UserId == actorUserId, cancellationToken);

        if (!actorInWorkspace)
        {
            WorkspaceId = null;

            if (targetUserId != actorUserId)
                return Forbid();

            var user = await _db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);

            if (user is null)
                return NotFound();

            BasicProfile = new BasicProfileVm
            {
                UserId = targetUserId,
                Email = user.Email ?? user.UserName ?? targetUserId,
                FullName = user.DisplayName ?? user.Email ?? user.UserName ?? targetUserId,
                AvatarUrl = user.AvatarUrl
            };

            return Page();
        }

        var targetInWorkspace = await _db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m => m.WorkspaceId == workspaceId.Value && m.UserId == targetUserId, cancellationToken);

        if (!targetInWorkspace)
            return NotFound();

        var isPm = await _db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m =>
                    m.WorkspaceId == workspaceId.Value &&
                    m.UserId == actorUserId &&
                    m.Role == WorkspaceMemberRole.PM,
                cancellationToken);

        var isPlatformAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == actorUserId && u.IsPlatformAdmin, cancellationToken);

        if (actorUserId != targetUserId && !isPm && !isPlatformAdmin)
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

        // UC-10: Nếu level thay đổi thì tạo đề xuất chờ Admin duyệt.
        // SubRole vẫn được PM cập nhật trực tiếp.
        var currentProfile = await _db.MemberProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == pmInput.TargetUserId, cancellationToken);
        var currentLevel = currentProfile?.Level ?? MemberLevel.Junior;

        bool levelChanged = pmInput.Level != currentLevel;
        if (levelChanged)
        {
            var propRes = await _levelAdjustment.ProposeLevelChangeAsync(
                workspaceId.Value,
                pmInput.TargetUserId,
                actorUserId,
                pmInput.Level,
                pmInput.LevelChangeReason ?? string.Empty,
                cancellationToken);

            if (!propRes.Success)
            {
                ModelState.AddModelError(string.Empty, propRes.ErrorMessage ?? "Đề xuất Level thất bại.");
                return await ReloadWithErrorAsync(workspaceId.Value, actorUserId, pmInput.TargetUserId, cancellationToken, pmInput: pmInput);
            }
            ToastMessage = "Đã gửi đề xuất thay đổi Level tới Admin.";
        }

        // Cập nhật SubRole (và giữ nguyên Level hiện tại trong DB)
        var result = await _memberProfile.UpdateLevelAsync(
            workspaceId.Value,
            actorUserId,
            pmInput.TargetUserId,
            currentLevel, // Giữ level cũ, chỉ cập nhật SubRole nếu có đổi
            string.IsNullOrWhiteSpace(pmInput.SubRole) ? null : pmInput.SubRole.Trim(),
            cancellationToken);

        if (!result.Success)
        {
            ModelState.AddModelError(string.Empty, result.ErrorMessage ?? "Cập nhật SubRole thất bại.");
            return await ReloadWithErrorAsync(workspaceId.Value, actorUserId, pmInput.TargetUserId, cancellationToken, pmInput: pmInput);
        }

        if (!levelChanged)
        {
            ToastMessage = "Đã cập nhật SubRole.";
        }

        return RedirectToPage("/Profile", new { userId = pmInput.TargetUserId });
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

        [MaxLength(500)]
        public string? LevelChangeReason { get; set; }
    }

    public sealed class BasicProfileVm
    {
        public required string UserId { get; init; }
        public required string Email { get; init; }
        public required string FullName { get; init; }
        public string? AvatarUrl { get; init; }
    }
}
