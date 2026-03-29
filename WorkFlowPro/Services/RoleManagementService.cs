using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;
using WorkFlowPro.ViewModels;
using TaskStatus = WorkFlowPro.Data.TaskStatus;

namespace WorkFlowPro.Services;

public sealed class RoleManagementService : IRoleManagementService
{
    private readonly WorkFlowProDbContext _db;
    private readonly INotificationService _notifications;
    private readonly IHubContext<TaskHub> _taskHub;
    private readonly ILogger<RoleManagementService> _logger;

    public RoleManagementService(
        WorkFlowProDbContext db,
        INotificationService notifications,
        IHubContext<TaskHub> taskHub,
        ILogger<RoleManagementService> logger)
    {
        _db = db;
        _notifications = notifications;
        _taskHub = taskHub;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WorkspaceMemberRoleRowVm>> GetWorkspaceMembersAsync(
        Guid workspaceId,
        string actorUserId,
        CancellationToken cancellationToken = default)
    {
        var isActorPlatformAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == actorUserId && u.IsPlatformAdmin, cancellationToken);

        var isActorPm = await _db.WorkspaceMembers.AsNoTracking()
            .AnyAsync(m =>
                    m.WorkspaceId == workspaceId &&
                    m.UserId == actorUserId &&
                    m.Role == WorkspaceMemberRole.PM,
                cancellationToken);

        var pmCount = await WorkspacePolicies.CountPmsAsync(_db, workspaceId, cancellationToken);

        var pendingPromote = (await _db.WorkspaceRoleChangeRequests.AsNoTracking()
            .Where(r =>
                r.WorkspaceId == workspaceId &&
                r.Status == WorkspaceRoleRequestStatus.Pending &&
                r.Kind == WorkspaceRoleRequestKind.PromoteMemberToPm)
            .Select(r => r.TargetUserId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var pendingDemote = (await _db.WorkspaceRoleChangeRequests.AsNoTracking()
            .Where(r =>
                r.WorkspaceId == workspaceId &&
                r.Status == WorkspaceRoleRequestStatus.Pending &&
                r.Kind == WorkspaceRoleRequestKind.DemotePmToMember)
            .Select(r => r.TargetUserId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var pendingRemovePm = (await _db.WorkspaceRoleChangeRequests.AsNoTracking()
            .Where(r =>
                r.WorkspaceId == workspaceId &&
                r.Status == WorkspaceRoleRequestStatus.Pending &&
                r.Kind == WorkspaceRoleRequestKind.RemovePmFromWorkspace)
            .Select(r => r.TargetUserId)
            .ToListAsync(cancellationToken)).ToHashSet();

        var rows = await (
            from wm in _db.WorkspaceMembers.AsNoTracking()
            where wm.WorkspaceId == workspaceId
            join u in _db.Users.AsNoTracking() on wm.UserId equals u.Id
            orderby wm.Role, u.DisplayName, u.Email
            select new { wm, u }
        ).ToListAsync(cancellationToken);

        return rows.Select(x =>
        {
            var canTouchOthers = x.wm.UserId != actorUserId;
            var showAdminRoleChange = canTouchOthers && isActorPlatformAdmin;
            var showPromote = canTouchOthers &&
                              isActorPm &&
                              !isActorPlatformAdmin &&
                              x.wm.Role == WorkspaceMemberRole.Member &&
                              pmCount < WorkspacePolicies.MaxPmsPerWorkspace &&
                              !pendingPromote.Contains(x.wm.UserId);
            var showDemoteReq = canTouchOthers &&
                                isActorPm &&
                                !isActorPlatformAdmin &&
                                x.wm.Role == WorkspaceMemberRole.PM &&
                                pmCount > 1 &&
                                !pendingDemote.Contains(x.wm.UserId);

            var showRemoveMember = canTouchOthers &&
                                   x.wm.Role == WorkspaceMemberRole.Member &&
                                   (isActorPm || isActorPlatformAdmin);

            var showRequestRemovePm = canTouchOthers &&
                                      x.wm.Role == WorkspaceMemberRole.PM &&
                                      isActorPm &&
                                      !isActorPlatformAdmin &&
                                      pmCount > 1 &&
                                      !pendingRemovePm.Contains(x.wm.UserId);

            return new WorkspaceMemberRoleRowVm(
                x.wm.UserId,
                x.u.DisplayName ?? x.u.Email ?? x.u.UserName ?? x.wm.UserId,
                x.u.Email ?? x.u.UserName ?? x.wm.UserId,
                x.u.AvatarUrl,
                x.wm.Role,
                x.wm.SubRole,
                CanChangeWorkspaceRole: canTouchOthers && (isActorPlatformAdmin || isActorPm),
                IsActorPlatformAdmin: isActorPlatformAdmin,
                ShowAdminRoleChangeForm: showAdminRoleChange,
                ShowPmRoleRequestForm: showPromote || showDemoteReq,
                HasPendingPromoteRequest: pendingPromote.Contains(x.wm.UserId),
                HasPendingDemoteRequest: pendingDemote.Contains(x.wm.UserId),
                HasPendingRemovePmRequest: pendingRemovePm.Contains(x.wm.UserId),
                ShowRemoveMemberFromWorkspace: showRemoveMember,
                ShowRequestRemovePmFromWorkspace: showRequestRemovePm);
        }).ToList();
    }

    public async Task<RoleManagementResult> ChangeRoleAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        WorkspaceMemberRole newRole,
        CancellationToken cancellationToken = default)
    {
        if (!await CanActorManageAsync(workspaceId, actorUserId, cancellationToken))
            return new RoleManagementResult(false, "Không có quyền quản lý role trong workspace này.");

        if (actorUserId == targetUserId)
            return new RoleManagementResult(false, "Không thể thay đổi workspace role của chính bạn.");

        var isPlatformAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == actorUserId && u.IsPlatformAdmin, cancellationToken);

        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null)
            return new RoleManagementResult(false, "Thành viên không tồn tại trong workspace.");

        var oldRole = member.Role;
        if (oldRole == newRole)
            return new RoleManagementResult(true);

        if (!isPlatformAdmin)
            return new RoleManagementResult(false,
                "PM không đổi trực tiếp PM/Member. Dùng \"Yêu cầu nâng/hạ PM\" hoặc liên hệ Admin.");

        if (oldRole == WorkspaceMemberRole.Member && newRole == WorkspaceMemberRole.PM)
        {
            var pmCount = await WorkspacePolicies.CountPmsAsync(_db, workspaceId, cancellationToken);
            if (pmCount >= WorkspacePolicies.MaxPmsPerWorkspace)
                return new RoleManagementResult(false,
                    $"Đơn vị chỉ tối đa {WorkspacePolicies.MaxPmsPerWorkspace} PM.");
        }

        if (oldRole == WorkspaceMemberRole.PM && newRole == WorkspaceMemberRole.Member)
        {
            var pmCount = await WorkspacePolicies.CountPmsAsync(_db, workspaceId, cancellationToken);
            if (pmCount <= 1)
                return new RoleManagementResult(false, "Đơn vị phải có ít nhất một PM.");
        }

        member.Role = newRole;
        if (newRole == WorkspaceMemberRole.PM)
            member.SubRole = WorkspaceMemberRole.PM.ToString();
        else if (newRole == WorkspaceMemberRole.Member && string.IsNullOrWhiteSpace(member.SubRole))
            member.SubRole = "Member";

        _db.WorkspaceMembers.Update(member);

        _db.RoleChangeLogs.Add(new RoleChangeLog
        {
            WorkspaceId = workspaceId,
            ChangedByUserId = actorUserId,
            TargetUserId = targetUserId,
            OldRole = oldRole,
            NewRole = newRole,
            TimestampUtc = DateTime.UtcNow
        });

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

        await _notifications.CreateAndPushAsync(
            userId: targetUserId,
            type: NotificationType.RoleChanged,
            message: $"Workspace role đã đổi: {oldRole} → {newRole}.",
            workspaceId: workspaceId,
            redirectUrl: "/Roles",
            cancellationToken: cancellationToken);

        await BroadcastWorkspaceDataChangedAsync(workspaceId, targetUserId, cancellationToken);

        return new RoleManagementResult(true);
    }

    public async Task<RoleManagementResult> ChangeSubRoleAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        string? subRole,
        CancellationToken cancellationToken = default)
    {
        if (!await CanActorManageAsync(workspaceId, actorUserId, cancellationToken))
            return new RoleManagementResult(false, "Không có quyền quản lý SubRole trong workspace này.");

        subRole = string.IsNullOrWhiteSpace(subRole) ? null : subRole.Trim();
        if (subRole is not null && subRole.Length > 100)
            return new RoleManagementResult(false, "SubRole tối đa 100 ký tự.");

        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null)
            return new RoleManagementResult(false, "Thành viên không tồn tại trong workspace.");

        var oldSub = member.SubRole;
        if (string.Equals(oldSub, subRole, StringComparison.Ordinal))
            return new RoleManagementResult(true);

        member.SubRole = subRole;
        _db.WorkspaceMembers.Update(member);

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

        await _notifications.CreateAndPushAsync(
            userId: targetUserId,
            type: NotificationType.RoleChanged,
            message: $"SubRole trong workspace đã cập nhật: {(oldSub ?? "—")} → {(subRole ?? "—")}.",
            workspaceId: workspaceId,
            redirectUrl: "/Roles",
            cancellationToken: cancellationToken);

        await BroadcastWorkspaceDataChangedAsync(workspaceId, targetUserId, cancellationToken);

        return new RoleManagementResult(true);
    }

    public async Task<RoleManagementResult> RemoveMemberFromWorkspaceAsync(
        Guid workspaceId,
        string actorUserId,
        string targetUserId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        if (!await CanActorManageAsync(workspaceId, actorUserId, cancellationToken))
            return new RoleManagementResult(false, "Bạn không có quyền xóa thành viên trong đơn vị này.");

        if (actorUserId == targetUserId)
            return new RoleManagementResult(false, "Không thể xóa chính bạn khỏi đơn vị từ đây.");

        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null)
            return new RoleManagementResult(false, "Thành viên không tồn tại trong đơn vị.");

        if (member.Role != WorkspaceMemberRole.Member)
            return new RoleManagementResult(false,
                "Chỉ xóa trực tiếp được thành viên Member. Để gỡ PM khác, dùng «Yêu cầu xóa PM» (chờ Admin duyệt).");

        return await ExecuteRemoveUserFromWorkspaceAsync(
            workspaceId,
            targetUserId,
            reason,
            actorUserId,
            excludeWorkspaceRoleRequestId: null,
            cancellationToken);
    }

    public async Task<RoleManagementResult> ExecuteRemoveUserFromWorkspaceAsync(
        Guid workspaceId,
        string targetUserId,
        string reason,
        string actionRecordedAsUserId,
        int? excludeWorkspaceRoleRequestId,
        CancellationToken cancellationToken = default)
    {
        reason = reason?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(reason))
            return new RoleManagementResult(false, "Lý do là bắt buộc.");
        if (reason.Length > 2000)
            return new RoleManagementResult(false, "Lý do tối đa 2000 ký tự.");

        var workspace = await _db.Workspaces.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workspaceId, cancellationToken);
        var workspaceDisplayName = workspace?.Name ?? workspaceId.ToString();

        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null)
            return new RoleManagementResult(false, "Thành viên không tồn tại trong đơn vị.");

        var rows = await (
            from a in _db.TaskAssignments
            join t in _db.Tasks on a.TaskId equals t.Id
            join p in _db.Projects on t.ProjectId equals p.Id
            where p.WorkspaceId == workspaceId
                  && a.AssigneeUserId == targetUserId
                  && (a.Status == TaskAssignmentStatus.Pending || a.Status == TaskAssignmentStatus.Accepted)
                  && t.Status != TaskStatus.Done
                  && t.Status != TaskStatus.Cancelled
            select new { a, t }
        ).ToListAsync(cancellationToken);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTime.UtcNow;

        var pendingRoleReqs = await _db.WorkspaceRoleChangeRequests
            .Where(r =>
                r.WorkspaceId == workspaceId &&
                r.Status == WorkspaceRoleRequestStatus.Pending &&
                (excludeWorkspaceRoleRequestId == null || r.Id != excludeWorkspaceRoleRequestId.Value) &&
                (r.TargetUserId == targetUserId || r.RequestedByUserId == targetUserId))
            .ToListAsync(cancellationToken);
        if (pendingRoleReqs.Count > 0)
            _db.WorkspaceRoleChangeRequests.RemoveRange(pendingRoleReqs);

        var workloadDecrement = 0;
        var reasonForHistory = reason.Length > 500 ? reason[..500] : reason;

        foreach (var g in rows.GroupBy(x => x.t.Id))
        {
            var task = g.First().t;

            if (g.Any(x => x.a.Status == TaskAssignmentStatus.Accepted))
                workloadDecrement++;

            foreach (var x in g)
                _db.TaskAssignments.Remove(x.a);

            task.Status = TaskStatus.Unassigned;
            task.UpdatedAtUtc = now;
            _db.Tasks.Update(task);

            _db.TaskHistoryEntries.Add(new TaskHistoryEntry
            {
                TaskId = task.Id,
                ActorUserId = actionRecordedAsUserId,
                Action = "Removed from workspace — task unassigned",
                OldValue = targetUserId,
                NewValue = reasonForHistory,
                TimestampUtc = now
            });
        }

        if (workloadDecrement > 0)
        {
            var profile = await _db.MemberProfiles.FirstOrDefaultAsync(p => p.UserId == targetUserId, cancellationToken);
            if (profile is not null)
                profile.CurrentWorkload = Math.Max(0, profile.CurrentWorkload - workloadDecrement);
        }

        _db.WorkspaceMembers.Remove(member);

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

        var reasonInMessage = reason.Length > 800 ? reason[..800] + "…" : reason;
        var message =
            $"Bạn đã bị xóa khỏi đơn vị «{workspaceDisplayName}». Lý do: {reasonInMessage}.";
        if (message.Length > 2000)
            message = message[..2000];

        await _notifications.CreateAndPushAsync(
            userId: targetUserId,
            type: NotificationType.RemovedFromWorkspace,
            message: message,
            workspaceId: null,
            redirectUrl: "/Workspaces",
            cancellationToken: cancellationToken);

        foreach (var g in rows.GroupBy(x => x.t.Id))
        {
            var task = g.First().t;
            try
            {
                await _taskHub.Clients.Group(task.ProjectId.ToString("D"))
                    .SendAsync(
                        "TaskUpdated",
                        new { taskId = task.Id, newStatus = TaskStatus.Unassigned.ToString() },
                        cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR TaskUpdated after remove member task {TaskId}", task.Id);
            }
        }

        await BroadcastWorkspaceDataChangedAsync(workspaceId, targetUserId, cancellationToken);

        return new RoleManagementResult(true);
    }

    /// <summary>UC-04: gợi ý phân công phụ thuộc MemberProfile + WorkspaceMember (SubRole / Role).</summary>
    private async Task BroadcastWorkspaceDataChangedAsync(
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
            _logger.LogWarning(ex, "SignalR memberProfileUpdated failed (UC-09) workspace {Ws}", workspaceId);
        }
    }

    private async Task<bool> CanActorManageAsync(Guid workspaceId, string actorUserId, CancellationToken cancellationToken)
    {
        var isAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == actorUserId && u.IsPlatformAdmin, cancellationToken);
        if (isAdmin)
            return true;

        return await _db.WorkspaceMembers.AnyAsync(m =>
                m.WorkspaceId == workspaceId &&
                m.UserId == actorUserId &&
                m.Role == WorkspaceMemberRole.PM,
            cancellationToken);
    }
}
