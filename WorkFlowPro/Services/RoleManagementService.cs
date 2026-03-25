using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;
using WorkFlowPro.Hubs;
using WorkFlowPro.ViewModels;

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
        var rows = await (
            from wm in _db.WorkspaceMembers.AsNoTracking()
            where wm.WorkspaceId == workspaceId
            join u in _db.Users.AsNoTracking() on wm.UserId equals u.Id
            orderby wm.Role, u.DisplayName, u.Email
            select new { wm, u }
        ).ToListAsync(cancellationToken);

        return rows.Select(x => new WorkspaceMemberRoleRowVm(
            x.wm.UserId,
            x.u.DisplayName ?? x.u.Email ?? x.u.UserName ?? x.wm.UserId,
            x.u.Email ?? x.u.UserName ?? x.wm.UserId,
            x.u.AvatarUrl,
            x.wm.Role,
            x.wm.SubRole,
            CanChangeWorkspaceRole: x.wm.UserId != actorUserId)).ToList();
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

        var member = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(m => m.WorkspaceId == workspaceId && m.UserId == targetUserId, cancellationToken);

        if (member is null)
            return new RoleManagementResult(false, "Thành viên không tồn tại trong workspace.");

        var oldRole = member.Role;
        if (oldRole == newRole)
            return new RoleManagementResult(true);

        if (oldRole == WorkspaceMemberRole.PM && newRole == WorkspaceMemberRole.Member)
        {
            var pmCount = await _db.WorkspaceMembers.CountAsync(m =>
                    m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.PM,
                cancellationToken);

            if (pmCount <= 1)
                return new RoleManagementResult(false, "Workspace phải có ít nhất một PM.");
        }

        member.Role = newRole;
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
