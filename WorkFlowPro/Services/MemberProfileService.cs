using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Services;

public sealed class MemberProfileService : IMemberProfileService
{
    public const long MaxAvatarBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> AllowedAvatarContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg",
        "image/png"
    };

    private readonly WorkFlowProDbContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly IHubContext<TaskHub> _taskHub;
    private readonly INotificationService _notifications;
    private readonly ILogger<MemberProfileService> _logger;

    public MemberProfileService(
        WorkFlowProDbContext db,
        IWebHostEnvironment env,
        IHubContext<TaskHub> taskHub,
        INotificationService notifications,
        ILogger<MemberProfileService> logger)
    {
        _db = db;
        _env = env;
        _taskHub = taskHub;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<MemberProfilePageVm?> GetProfilePageAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(targetUserId))
            return null;

        var targetMembership = await _db.WorkspaceMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (targetMembership is null)
            return null;

        var isPm = await _db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m =>
                    m.WorkspaceId == workspaceId &&
                    m.UserId == actorUserId &&
                    m.Role == WorkspaceMemberRole.PM,
                cancellationToken);

        var isPlatformAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == actorUserId && u.IsPlatformAdmin, cancellationToken);

        if (actorUserId != targetUserId && !isPm && !isPlatformAdmin)
            return null;

        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);

        if (user is null)
            return null;

        var profile = await _db.MemberProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == targetUserId, cancellationToken);

        profile ??= new MemberProfile { UserId = targetUserId };

        var cutoffUtc = DateTime.UtcNow.AddDays(-30);

        var taskRows = await (
            from a in _db.TaskAssignments.AsNoTracking()
            join t in _db.Tasks.AsNoTracking() on a.TaskId equals t.Id
            where a.AssigneeUserId == targetUserId && t.UpdatedAtUtc >= cutoffUtc
            orderby t.UpdatedAtUtc descending
            select new { t.Id, t.Title, t.Status, t.DueDateUtc }
        ).Take(200).ToListAsync(cancellationToken);

        var taskIds = taskRows.Select(r => r.Id).ToList();
        IReadOnlyDictionary<Guid, int> scoreByTaskId = new Dictionary<Guid, int>();
        if (taskIds.Count > 0)
        {
            var evals = await _db.TaskEvaluations.AsNoTracking()
                .Where(e => taskIds.Contains(e.TaskId))
                .Select(e => new { e.TaskId, e.EvaluatedAtUtc, e.Score })
                .ToListAsync(cancellationToken);

            scoreByTaskId = evals
                .GroupBy(e => e.TaskId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.EvaluatedAtUtc).First().Score);
        }

        var history = taskRows
            .Select(r => new ProfileTaskHistoryRowVm(
                r.Id,
                r.Title,
                r.Status,
                r.DueDateUtc,
                scoreByTaskId.TryGetValue(r.Id, out var s) ? s : null))
            .ToList();

        var isSelf = actorUserId == targetUserId;
        var canEditProfile = isSelf;
        var canEditLevelOrSubRole = isPm && !isSelf && targetMembership.Role == WorkspaceMemberRole.Member;

        return new MemberProfilePageVm
        {
            TargetUserId = targetUserId,
            Email = user.Email ?? user.UserName ?? targetUserId,
            FullName = user.DisplayName ?? user.Email ?? user.UserName ?? targetUserId,
            AvatarUrl = user.AvatarUrl,
            SubRole = targetMembership.SubRole,
            WorkspaceRole = targetMembership.Role,
            Level = profile.Level,
            CompletionRate = profile.CompletionRate,
            AvgScore = profile.AvgScore,
            CurrentWorkload = profile.CurrentWorkload,
            IsSelf = isSelf,
            IsPm = isPm,
            CanEditProfile = canEditProfile,
            CanEditLevelOrSubRole = canEditLevelOrSubRole,
            IsPlatformAdminReadOnlyView = isPlatformAdmin && !isSelf,
            TaskHistory = history
        };
    }

    public async Task<MemberProfileResult> UpdateProfileAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        string fullName,
        IFormFile? avatarFile,
        CancellationToken cancellationToken = default)
    {
        if (actorUserId != targetUserId)
            return new MemberProfileResult(false, "Chỉ được chỉnh sửa profile của chính bạn.");

        var membership = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == actorUserId, cancellationToken);

        if (membership is null)
            return new MemberProfileResult(false, "Bạn không thuộc workspace hiện tại.");

        fullName = fullName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fullName))
            return new MemberProfileResult(false, "Họ tên không được để trống.");

        if (fullName.Length > 200)
            return new MemberProfileResult(false, "Họ tên tối đa 200 ký tự.");

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == targetUserId, cancellationToken);
        if (user is null)
            return new MemberProfileResult(false, "Không tìm thấy user.");

        user.DisplayName = fullName;

        if (avatarFile is not null && avatarFile.Length > 0)
        {
            var val = await ValidateAvatarAsync(avatarFile, cancellationToken);
            if (val is not null)
                return new MemberProfileResult(false, val);

            var relativeUrl = await SaveAvatarAsync(targetUserId, avatarFile, cancellationToken);
            user.AvatarUrl = relativeUrl;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        return new MemberProfileResult(true);
    }

    public async Task<MemberProfileResult> UpdateLevelAsync(
        Guid workspaceId,
        string pmUserId,
        string targetUserId,
        MemberLevel newLevel,
        string? subRole,
        CancellationToken cancellationToken = default)
    {
        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
                m.WorkspaceId == workspaceId &&
                m.UserId == pmUserId &&
                m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        if (!isPm)
            return new MemberProfileResult(false, "Chỉ PM mới được chỉnh Level/SubRole.");

        if (pmUserId == targetUserId)
            return new MemberProfileResult(false, "Không thể chỉnh Level/SubRole cho chính mình qua form này.");

        var wm = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (wm is null)
            return new MemberProfileResult(false, "Member không thuộc workspace.");

        if (wm.Role != WorkspaceMemberRole.Member)
            return new MemberProfileResult(false, "Chỉ áp dụng cho thành viên có vai trò Member.");

        subRole = string.IsNullOrWhiteSpace(subRole) ? null : subRole.Trim();
        if (subRole is not null && subRole.Length > 100)
            return new MemberProfileResult(false, "SubRole tối đa 100 ký tự.");

        var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == targetUserId, cancellationToken);
        profile ??= new MemberProfile { UserId = targetUserId };
        if (_db.Entry(profile).State == EntityState.Detached)
            _db.MemberProfiles.Add(profile);

        var oldLevel = profile.Level;
        var oldSub = wm.SubRole;
        var levelChanged = oldLevel != newLevel;
        var subRoleChanged = !string.Equals(oldSub, subRole, StringComparison.Ordinal);

        if (!levelChanged && !subRoleChanged)
            return new MemberProfileResult(true);

        profile.Level = newLevel;
        wm.SubRole = subRole;

        if (levelChanged)
        {
            _db.LevelChangeLogs.Add(new LevelChangeLog
            {
                TargetUserId = targetUserId,
                ChangedByPmId = pmUserId,
                OldLevel = oldLevel,
                NewLevel = newLevel,
                WorkspaceId = workspaceId,
                ChangedAt = DateTime.UtcNow
            });
        }

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }

        // UC-11: sau khi commit DB — tránh gửi thông báo khi lưu thất bại.
        if (levelChanged)
        {
            var msg = subRoleChanged
                ? $"PM đã cập nhật Level: {oldLevel} → {newLevel}; SubRole: {(oldSub ?? "—")} → {(subRole ?? "—")}."
                : $"PM đã đổi Level của bạn: {oldLevel} → {newLevel}.";

            await _notifications.CreateAndPushAsync(
                userId: targetUserId,
                type: NotificationType.LevelChanged,
                message: msg,
                workspaceId: workspaceId,
                redirectUrl: "/Profile",
                cancellationToken: cancellationToken);
        }
        else if (subRoleChanged)
        {
            await _notifications.CreateAndPushAsync(
                userId: targetUserId,
                type: NotificationType.RoleChanged,
                message: $"SubRole trong workspace đã cập nhật: {(oldSub ?? "—")} → {(subRole ?? "—")}.",
                workspaceId: workspaceId,
                redirectUrl: "/Profile",
                cancellationToken: cancellationToken);
        }

        try
        {
            await _taskHub.Clients.Group(TaskHub.WorkspaceGroupName(workspaceId))
                .SendAsync("memberProfileUpdated", targetUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR memberProfileUpdated failed for workspace {Ws}", workspaceId);
        }

        return new MemberProfileResult(true);
    }

    private async Task<string?> ValidateAvatarAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length > MaxAvatarBytes)
            return "Avatar tối đa 2MB.";

        var ct = file.ContentType ?? string.Empty;
        if (!AllowedAvatarContentTypes.Contains(ct))
            return "Avatar chỉ chấp nhận JPG hoặc PNG.";

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not ".jpg" and not ".jpeg" and not ".png")
            return "Định dạng file phải là .jpg, .jpeg hoặc .png.";

        await using var stream = file.OpenReadStream();
        var header = new byte[8];
        var n = await stream.ReadAsync(header.AsMemory(0, 8), cancellationToken);
        if (n < 3)
            return "File ảnh không hợp lệ.";

        var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        var isPng = n >= 8 &&
                    header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                    header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;

        if (!isJpeg && !isPng)
            return "Nội dung file không phải JPG/PNG hợp lệ.";

        return null;
    }

    private async Task<string> SaveAvatarAsync(string userId, IFormFile file, CancellationToken cancellationToken)
    {
        var uploads = Path.Combine(_env.WebRootPath, "uploads", "avatars");
        Directory.CreateDirectory(uploads);

        await using var readStream = file.OpenReadStream();
        var header = new byte[8];
        var n = await readStream.ReadAsync(header.AsMemory(0, 8), cancellationToken);
        readStream.Position = 0;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(ext) || ext.Length > 10)
        {
            var isPng = n >= 8 &&
                        header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                        header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
            ext = isPng ? ".png" : ".jpg";
        }

        var safeName = $"{userId}_{Guid.NewGuid():N}{ext}";
        var physical = Path.Combine(uploads, safeName);

        await using (var outStream = File.Create(physical))
        {
            await readStream.CopyToAsync(outStream, cancellationToken);
        }

        return $"/uploads/avatars/{safeName}".Replace('\\', '/');
    }
}
