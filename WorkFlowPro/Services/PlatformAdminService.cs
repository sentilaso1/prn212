using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;

namespace WorkFlowPro.Services;

public sealed class PlatformAdminService : IPlatformAdminService
{
    private readonly WorkFlowProDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IWorkspaceOnboardingService _workspaceOnboarding;
    private readonly INotificationService _notifications;
    private readonly IHubContext<TaskHub> _taskHub;
    private readonly ILogger<PlatformAdminService> _logger;

    public PlatformAdminService(
        WorkFlowProDbContext db,
        UserManager<ApplicationUser> userManager,
        IWorkspaceOnboardingService workspaceOnboarding,
        INotificationService notifications,
        IHubContext<TaskHub> taskHub,
        ILogger<PlatformAdminService> logger)
    {
        _db = db;
        _userManager = userManager;
        _workspaceOnboarding = workspaceOnboarding;
        _notifications = notifications;
        _taskHub = taskHub;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PendingPmRegistrationVm>> GetPendingPmRegistrationsAsync(
        CancellationToken cancellationToken = default)
    {
        var list = await _db.Users.AsNoTracking()
            .Where(u =>
                u.AwaitingPmWorkspaceApproval &&
                u.AccountStatus == AccountStatus.PendingApproval &&
                !u.IsPlatformAdmin)
            .OrderBy(u => u.Email)
            .Select(u => new PendingPmRegistrationVm(
                u.Id,
                u.Email ?? u.UserName ?? u.Id,
                u.DisplayName,
                u.PendingWorkspaceName,
                u.LockoutEnd))
            .ToListAsync(cancellationToken);

        return list;
    }

    public async Task<AdminActionResult> ApprovePmRegistrationAsync(
        string adminUserId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsPlatformAdminAsync(adminUserId, cancellationToken))
            return new AdminActionResult(false, "Chỉ Platform Admin mới được duyệt.");

        var user = await _userManager.FindByIdAsync(targetUserId);
        if (user is null)
            return new AdminActionResult(false, "Không tìm thấy user.");

        if (!user.AwaitingPmWorkspaceApproval || user.AccountStatus != AccountStatus.PendingApproval)
            return new AdminActionResult(false, "Tài khoản không nằm trong hàng đợi duyệt PM.");

        var baseName = string.IsNullOrWhiteSpace(user.PendingWorkspaceName)
            ? user.DisplayName ?? user.Email?.Split('@')[0] ?? "Đơn vị"
            : user.PendingWorkspaceName.Trim();

        var workspaceName = $"Đơn vị — {baseName}";
        if (workspaceName.Length > 200)
            workspaceName = workspaceName[..200];

        try
        {
            await _workspaceOnboarding.CreateWorkspaceAndBootstrapUserAsync(
                user.Id,
                workspaceName,
                cancellationToken);

            user.AccountStatus = AccountStatus.Approved;
            user.AwaitingPmWorkspaceApproval = false;
            user.PendingWorkspaceName = null;
            var updateRes = await _userManager.UpdateAsync(user);
            if (!updateRes.Succeeded)
            {
                _logger.LogError(
                    "ApprovePmRegistration: workspace created but user update failed for {UserId}",
                    targetUserId);
                return new AdminActionResult(false, updateRes.Errors.FirstOrDefault()?.Description ?? "Cập nhật user thất bại.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ApprovePmRegistration failed for {UserId}", targetUserId);
            return new AdminActionResult(false, "Không tạo được đơn vị. Thử lại sau.");
        }

        await _notifications.CreateAndPushAsync(
            targetUserId,
            NotificationType.RegistrationPendingPm,
            $"Admin đã duyệt. Đơn vị \"{workspaceName}\" đã được tạo — bạn là PM.",
            workspaceId: null,
            redirectUrl: "/Workspaces",
            cancellationToken: cancellationToken);

        return new AdminActionResult(true);
    }

    public async Task<AdminActionResult> RejectPmRegistrationAsync(
        string adminUserId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsPlatformAdminAsync(adminUserId, cancellationToken))
            return new AdminActionResult(false, "Chỉ Platform Admin mới được từ chối.");

        var user = await _userManager.FindByIdAsync(targetUserId);
        if (user is null)
            return new AdminActionResult(false, "Không tìm thấy user.");

        if (!user.AwaitingPmWorkspaceApproval)
            return new AdminActionResult(false, "Không có yêu cầu PM đang chờ.");

        user.AccountStatus = AccountStatus.Rejected;
        user.AwaitingPmWorkspaceApproval = false;
        user.PendingWorkspaceName = null;

        var updateRes = await _userManager.UpdateAsync(user);
        if (!updateRes.Succeeded)
            return new AdminActionResult(false, updateRes.Errors.FirstOrDefault()?.Description ?? "Lỗi.");

        await _notifications.CreateAndPushAsync(
            targetUserId,
            NotificationType.RegistrationPendingPm,
            "Yêu cầu tạo đơn vị (PM) đã bị Admin từ chối.",
            workspaceId: null,
            redirectUrl: "/Identity/Account/Login",
            cancellationToken: cancellationToken);

        return new AdminActionResult(true);
    }

    public async Task<IReadOnlyList<WorkspaceRoleRequestListVm>> GetPendingWorkspaceRoleRequestsAsync(
        CancellationToken cancellationToken = default)
    {
        var q = from r in _db.WorkspaceRoleChangeRequests.AsNoTracking()
            join w in _db.Workspaces.AsNoTracking() on r.WorkspaceId equals w.Id
            join tu in _db.Users.AsNoTracking() on r.TargetUserId equals tu.Id
            join ru in _db.Users.AsNoTracking() on r.RequestedByUserId equals ru.Id
            where r.Status == WorkspaceRoleRequestStatus.Pending
            orderby r.CreatedAtUtc
            select new WorkspaceRoleRequestListVm(
                r.Id,
                r.WorkspaceId,
                w.Name,
                r.Kind,
                r.TargetUserId,
                tu.DisplayName ?? tu.Email ?? tu.UserName ?? r.TargetUserId,
                r.RequestedByUserId,
                ru.DisplayName ?? ru.Email ?? ru.UserName ?? r.RequestedByUserId,
                r.Reason,
                r.CreatedAtUtc);

        return await q.ToListAsync(cancellationToken);
    }

    public async Task<AdminActionResult> ApproveWorkspaceRoleRequestAsync(
        string adminUserId,
        int requestId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsPlatformAdminAsync(adminUserId, cancellationToken))
            return new AdminActionResult(false, "Chỉ Platform Admin.");

        var req = await _db.WorkspaceRoleChangeRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (req is null || req.Status != WorkspaceRoleRequestStatus.Pending)
            return new AdminActionResult(false, "Yêu cầu không hợp lệ.");

        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(
                m => m.WorkspaceId == req.WorkspaceId && m.UserId == req.TargetUserId,
                cancellationToken);

        if (member is null)
            return new AdminActionResult(false, "Thành viên không thuộc đơn vị.");

        WorkspaceMemberRole oldRole;
        if (req.Kind == WorkspaceRoleRequestKind.PromoteMemberToPm)
        {
            if (member.Role == WorkspaceMemberRole.PM)
                return new AdminActionResult(false, "User đã là PM.");

            var pmCount = await WorkspacePolicies.CountPmsAsync(_db, req.WorkspaceId, cancellationToken);
            if (pmCount >= WorkspacePolicies.MaxPmsPerWorkspace)
                return new AdminActionResult(false, $"Đơn vị đã đủ {WorkspacePolicies.MaxPmsPerWorkspace} PM.");

            oldRole = member.Role;
            member.Role = WorkspaceMemberRole.PM;
            member.SubRole = WorkspaceMemberRole.PM.ToString();
        }
        else if (req.Kind == WorkspaceRoleRequestKind.DemotePmToMember)
        {
            if (member.Role != WorkspaceMemberRole.PM)
                return new AdminActionResult(false, "Chỉ hạ PM xuống Member.");

            var pmCount = await WorkspacePolicies.CountPmsAsync(_db, req.WorkspaceId, cancellationToken);
            if (pmCount <= 1)
                return new AdminActionResult(false, "Phải còn ít nhất một PM trong đơn vị.");

            oldRole = member.Role;
            member.Role = WorkspaceMemberRole.Member;
            if (string.IsNullOrWhiteSpace(member.SubRole))
                member.SubRole = "Member";
        }
        else
            return new AdminActionResult(false, "Loại yêu cầu không hỗ trợ.");

        _db.RoleChangeLogs.Add(new RoleChangeLog
        {
            WorkspaceId = req.WorkspaceId,
            ChangedByUserId = adminUserId,
            TargetUserId = req.TargetUserId,
            OldRole = oldRole,
            NewRole = member.Role,
            TimestampUtc = DateTime.UtcNow
        });

        req.Status = WorkspaceRoleRequestStatus.Approved;
        req.ReviewedAtUtc = DateTime.UtcNow;
        req.ReviewedByAdminId = adminUserId;

        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.CreateAndPushAsync(
            req.TargetUserId,
            NotificationType.WorkspacePmRoleRequest,
            $"Admin đã duyệt thay đổi vai trò trong đơn vị ({req.Kind}).",
            workspaceId: req.WorkspaceId,
            redirectUrl: "/Workspaces",
            cancellationToken: cancellationToken);

        await _notifications.CreateAndPushAsync(
            req.RequestedByUserId,
            NotificationType.WorkspacePmRoleRequest,
            $"Yêu cầu #{req.Id} đã được Admin duyệt.",
            workspaceId: req.WorkspaceId,
            redirectUrl: "/Roles",
            cancellationToken: cancellationToken);

        await BroadcastWorkspaceAsync(req.WorkspaceId, req.TargetUserId, cancellationToken);

        return new AdminActionResult(true);
    }

    public async Task<AdminActionResult> RejectWorkspaceRoleRequestAsync(
        string adminUserId,
        int requestId,
        string? adminNote,
        CancellationToken cancellationToken = default)
    {
        if (!await IsPlatformAdminAsync(adminUserId, cancellationToken))
            return new AdminActionResult(false, "Chỉ Platform Admin.");

        var req = await _db.WorkspaceRoleChangeRequests
            .FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);

        if (req is null || req.Status != WorkspaceRoleRequestStatus.Pending)
            return new AdminActionResult(false, "Yêu cầu không hợp lệ.");

        req.Status = WorkspaceRoleRequestStatus.Rejected;
        req.ReviewedAtUtc = DateTime.UtcNow;
        req.ReviewedByAdminId = adminUserId;
        req.AdminNote = string.IsNullOrWhiteSpace(adminNote) ? null : adminNote.Trim();

        await _db.SaveChangesAsync(cancellationToken);

        var note = string.IsNullOrWhiteSpace(req.AdminNote) ? string.Empty : $" Ghi chú: {req.AdminNote}";
        await _notifications.CreateAndPushAsync(
            req.RequestedByUserId,
            NotificationType.WorkspacePmRoleRequest,
            $"Yêu cầu #{req.Id} bị từ chối.{note}",
            workspaceId: req.WorkspaceId,
            redirectUrl: "/Roles",
            cancellationToken: cancellationToken);

        return new AdminActionResult(true);
    }

    public async Task<AdminActionResult> DemotePmDirectAsync(
        string adminUserId,
        Guid workspaceId,
        string targetUserId,
        CancellationToken cancellationToken = default)
    {
        if (!await IsPlatformAdminAsync(adminUserId, cancellationToken))
            return new AdminActionResult(false, "Chỉ Platform Admin.");

        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null || member.Role != WorkspaceMemberRole.PM)
            return new AdminActionResult(false, "Đối tượng không phải PM trong đơn vị này.");

        var pmCount = await WorkspacePolicies.CountPmsAsync(_db, workspaceId, cancellationToken);
        if (pmCount <= 1)
            return new AdminActionResult(false, "Không thể hạ PM cuối cùng.");

        var oldRole = member.Role;
        member.Role = WorkspaceMemberRole.Member;
        if (string.IsNullOrWhiteSpace(member.SubRole))
            member.SubRole = "Member";

        _db.RoleChangeLogs.Add(new RoleChangeLog
        {
            WorkspaceId = workspaceId,
            ChangedByUserId = adminUserId,
            TargetUserId = targetUserId,
            OldRole = oldRole,
            NewRole = WorkspaceMemberRole.Member,
            TimestampUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.CreateAndPushAsync(
            targetUserId,
            NotificationType.RoleChanged,
            "Admin đã hạ bạn từ PM xuống Member trong đơn vị.",
            workspaceId: workspaceId,
            redirectUrl: "/Workspaces",
            cancellationToken: cancellationToken);

        await BroadcastWorkspaceAsync(workspaceId, targetUserId, cancellationToken);

        return new AdminActionResult(true);
    }

    public async Task<AdminActionResult> SubmitPromoteToPmRequestAsync(
        string pmUserId,
        Guid workspaceId,
        string targetUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
                m.WorkspaceId == workspaceId &&
                m.UserId == pmUserId &&
                m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        if (!isPm)
            return new AdminActionResult(false, "Chỉ PM mới gửi được yêu cầu nâng.");

        if (pmUserId == targetUserId)
            return new AdminActionResult(false, "Không thể yêu cầu cho chính bạn.");

        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null || member.Role != WorkspaceMemberRole.Member)
            return new AdminActionResult(false, "Chỉ nâng Member lên PM.");

        var pmCount = await WorkspacePolicies.CountPmsAsync(_db, workspaceId, cancellationToken);
        if (pmCount >= WorkspacePolicies.MaxPmsPerWorkspace)
            return new AdminActionResult(false, $"Đơn vị đã đủ {WorkspacePolicies.MaxPmsPerWorkspace} PM.");

        var dup = await _db.WorkspaceRoleChangeRequests.AnyAsync(r =>
                r.WorkspaceId == workspaceId &&
                r.TargetUserId == targetUserId &&
                r.Kind == WorkspaceRoleRequestKind.PromoteMemberToPm &&
                r.Status == WorkspaceRoleRequestStatus.Pending,
            cancellationToken);

        if (dup)
            return new AdminActionResult(false, "Đã có yêu cầu nâng PM đang chờ cho user này.");

        _db.WorkspaceRoleChangeRequests.Add(new WorkspaceRoleChangeRequest
        {
            WorkspaceId = workspaceId,
            TargetUserId = targetUserId,
            RequestedByUserId = pmUserId,
            Kind = WorkspaceRoleRequestKind.PromoteMemberToPm,
            Status = WorkspaceRoleRequestStatus.Pending,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAdminsOfNewRequestAsync(workspaceId, "nâng Member lên PM", cancellationToken);

        return new AdminActionResult(true);
    }

    public async Task<AdminActionResult> SubmitDemotePmRequestAsync(
        string pmUserId,
        Guid workspaceId,
        string targetUserId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
                m.WorkspaceId == workspaceId &&
                m.UserId == pmUserId &&
                m.Role == WorkspaceMemberRole.PM,
            cancellationToken);

        if (!isPm)
            return new AdminActionResult(false, "Chỉ PM mới gửi được yêu cầu hạ.");

        if (pmUserId == targetUserId)
            return new AdminActionResult(false, "Không áp dụng cho chính bạn.");

        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null || member.Role != WorkspaceMemberRole.PM)
            return new AdminActionResult(false, "Chỉ hạ một PM khác.");

        var pmCount = await WorkspacePolicies.CountPmsAsync(_db, workspaceId, cancellationToken);
        if (pmCount <= 1)
            return new AdminActionResult(false, "Không thể hạ PM cuối cùng.");

        var dup = await _db.WorkspaceRoleChangeRequests.AnyAsync(r =>
                r.WorkspaceId == workspaceId &&
                r.TargetUserId == targetUserId &&
                r.Kind == WorkspaceRoleRequestKind.DemotePmToMember &&
                r.Status == WorkspaceRoleRequestStatus.Pending,
            cancellationToken);

        if (dup)
            return new AdminActionResult(false, "Đã có yêu cầu hạ PM đang chờ.");

        _db.WorkspaceRoleChangeRequests.Add(new WorkspaceRoleChangeRequest
        {
            WorkspaceId = workspaceId,
            TargetUserId = targetUserId,
            RequestedByUserId = pmUserId,
            Kind = WorkspaceRoleRequestKind.DemotePmToMember,
            Status = WorkspaceRoleRequestStatus.Pending,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            CreatedAtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);

        await NotifyAdminsOfNewRequestAsync(workspaceId, "hạ PM xuống Member", cancellationToken);

        return new AdminActionResult(true);
    }

    public async Task<IReadOnlyList<AdminWorkspaceListItemVm>> ListAllWorkspacesAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.Workspaces.AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new AdminWorkspaceListItemVm(w.Id, w.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminPmRowVm>> ListPmsInWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        return await (
            from m in _db.WorkspaceMembers.AsNoTracking()
            where m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM
            join u in _db.Users.AsNoTracking() on m.UserId equals u.Id
            orderby u.DisplayName, u.Email
            select new AdminPmRowVm(
                m.UserId,
                u.DisplayName ?? u.Email ?? u.UserName ?? m.UserId,
                u.Email ?? u.UserName ?? m.UserId)
        ).ToListAsync(cancellationToken);
    }

    private async Task<bool> IsPlatformAdminAsync(string userId, CancellationToken cancellationToken) =>
        await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsPlatformAdmin, cancellationToken);

    private async Task NotifyAdminsOfNewRequestAsync(
        Guid workspaceId,
        string actionLabel,
        CancellationToken cancellationToken)
    {
        var wsName = await _db.Workspaces.AsNoTracking()
            .Where(w => w.Id == workspaceId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync(cancellationToken);

        var adminIds = await _db.Users.AsNoTracking()
            .Where(u => u.IsPlatformAdmin)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var aid in adminIds)
        {
            await _notifications.CreateAndPushAsync(
                aid,
                NotificationType.WorkspacePmRoleRequest,
                $"Có yêu cầu {actionLabel} trong đơn vị \"{wsName ?? ""}\".",
                workspaceId: workspaceId,
                redirectUrl: "/Admin",
                cancellationToken: cancellationToken);
        }
    }

    private async Task BroadcastWorkspaceAsync(
        Guid workspaceId,
        string targetUserId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _taskHub.Clients.Group(TaskHub.WorkspaceGroupName(workspaceId))
                .SendAsync("memberProfileUpdated", targetUserId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SignalR memberProfileUpdated failed after admin role change");
        }
    }
}
