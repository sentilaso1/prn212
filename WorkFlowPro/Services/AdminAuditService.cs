using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public sealed class AdminAuditService : IAdminAuditService
{
    private readonly WorkFlowProDbContext _db;

    public AdminAuditService(WorkFlowProDbContext db)
    {
        _db = db;
    }

    public async Task AppendAsync(
        string actorUserId,
        AdminAuditActionType actionType,
        string targetSummary,
        string? notes = null,
        string? targetUserId = null,
        Guid? targetProjectId = null,
        Guid? workspaceId = null,
        CancellationToken cancellationToken = default)
    {
        var summary = string.IsNullOrWhiteSpace(targetSummary) ? "—" : targetSummary.Trim();
        if (summary.Length > 500)
            summary = summary[..500];

        notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        if (notes is { Length: > 2000 })
            notes = notes[..2000];

        _db.AdminAuditLogs.Add(new AdminAuditLog
        {
            ActorUserId = actorUserId,
            ActionType = actionType.ToString(),
            TargetSummary = summary,
            TargetUserId = targetUserId,
            TargetProjectId = targetProjectId,
            WorkspaceId = workspaceId,
            Notes = notes,
            TimestampUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AdminAuditLogRowVm>> QueryAsync(
        AdminAuditLogQuery query,
        CancellationToken cancellationToken = default)
    {
        var q = _db.AdminAuditLogs.AsNoTracking().AsQueryable();

        if (query.ActionType is { } at)
            q = q.Where(e => e.ActionType == at.ToString());

        if (query.FromUtcInclusive is { } from)
            q = q.Where(e => e.TimestampUtc >= from);

        if (query.ToUtcInclusive is { } to)
            q = q.Where(e => e.TimestampUtc <= to);

        if (!string.IsNullOrWhiteSpace(query.TargetUserIdFilter))
        {
            var tid = query.TargetUserIdFilter.Trim();
            q = q.Where(e => e.TargetUserId == tid || e.ActorUserId == tid);
        }

        if (!string.IsNullOrWhiteSpace(query.Keyword))
        {
            var kw = query.Keyword.Trim();
            var pattern = $"%{kw}%";
            q = q.Where(e =>
                EF.Functions.Like(e.TargetSummary, pattern) ||
                (e.Notes != null && EF.Functions.Like(e.Notes, pattern)) ||
                _db.Users.Any(u =>
                    u.Id == e.ActorUserId &&
                    ((u.Email != null && EF.Functions.Like(u.Email, pattern)) ||
                     (u.UserName != null && EF.Functions.Like(u.UserName, pattern)) ||
                     (u.DisplayName != null && EF.Functions.Like(u.DisplayName, pattern)))));
        }

        var rows = await (
            from e in q
            join u in _db.Users.AsNoTracking() on e.ActorUserId equals u.Id into uj
            from actor in uj.DefaultIfEmpty()
            orderby e.TimestampUtc descending
            select new { e, actor }
        ).Take(500).ToListAsync(cancellationToken);

        return rows.Select(x => MapRow(x.e, x.actor)).ToList();
    }

    private static AdminAuditLogRowVm MapRow(AdminAuditLog e, Auth.ApplicationUser? actor)
    {
        var parsed = Enum.TryParse<AdminAuditActionType>(e.ActionType, out var at)
            ? at
            : AdminAuditActionType.PmRegistrationApproved;

        var actorName = actor != null
            ? (actor.DisplayName ?? actor.Email ?? actor.UserName ?? e.ActorUserId)
            : e.ActorUserId;

        return new AdminAuditLogRowVm(
            e.Id,
            e.ActorUserId,
            actorName,
            parsed,
            ActionLabel(parsed),
            e.TargetSummary,
            e.Notes,
            e.TimestampUtc);
    }

    private static string ActionLabel(AdminAuditActionType t) => t switch
    {
        AdminAuditActionType.PmRegistrationApproved => "Duyệt đăng ký PM",
        AdminAuditActionType.PmRegistrationRejected => "Từ chối đăng ký PM",
        AdminAuditActionType.LevelAdjustmentApproved => "Duyệt đổi level",
        AdminAuditActionType.LevelAdjustmentRejected => "Từ chối đổi level",
        AdminAuditActionType.PmDemotedToMember => "Hạ PM → Member",
        AdminAuditActionType.ProjectCreationApproved => "Duyệt tạo dự án",
        AdminAuditActionType.ProjectCreationRejected => "Từ chối tạo dự án",
        AdminAuditActionType.MemberRemovedFromWorkspace => "Xóa member khỏi đơn vị",
        _ => t.ToString()
    };
}
