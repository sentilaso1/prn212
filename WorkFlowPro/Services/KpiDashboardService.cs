using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

using TaskStatus = WorkFlowPro.Data.TaskStatus;

public interface IKpiDashboardService
{
    /// <summary>
    /// UC-13: KPI project trong khoảng thời gian. Trả null nếu không phải PM hoặc project không thuộc workspace.
    /// </summary>
    Task<ProjectDashboardVm?> GetProjectDashboardAsync(
        string pmUserId,
        Guid workspaceId,
        Guid projectId,
        DateTime fromUtcInclusive,
        DateTime toUtcInclusive,
        CancellationToken cancellationToken = default);

    /// <summary>Danh sách project trong workspace (PM đã được kiểm tra).</summary>
    Task<IReadOnlyList<ProjectListItemVm>> ListProjectsForPmAsync(
        string pmUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    /// <summary>UC-09 Path C: Tổng quan tất cả workspace (Platform Admin).</summary>
    Task<IReadOnlyList<WorkspaceOverviewRowVm>> ListWorkspacesOverviewAsync(
        CancellationToken cancellationToken = default);

    /// <summary>UC-09 Path C: Project trong workspace — không kiểm tra PM.</summary>
    Task<IReadOnlyList<ProjectListItemVm>> ListProjectsInWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    /// <summary>UC-09 Path C: KPI project — không kiểm tra PM (caller đã authorize).</summary>
    Task<ProjectDashboardVm?> GetProjectDashboardForWorkspaceAsync(
        Guid workspaceId,
        Guid projectId,
        DateTime fromUtcInclusive,
        DateTime toUtcInclusive,
        CancellationToken cancellationToken = default);
}

public sealed record WorkspaceOverviewRowVm(
    Guid WorkspaceId,
    string WorkspaceName,
    int MemberCount,
    int ProjectCount,
    int TotalTasks,
    int CompletedTasks,
    /// <summary>Trung bình CompletionRate (0–1) của member trong workspace.</summary>
    decimal AvgMemberCompletionRate);

public sealed record ProjectListItemVm(Guid Id, string Name);

public sealed class ProjectDashboardVm
{
    public Guid ProjectId { get; init; }
    public string ProjectName { get; init; } = string.Empty;
    public DateTime FromUtc { get; init; }
    public DateTime ToUtc { get; init; }

    public int TotalTasks { get; init; }
    public int CompletedTasks { get; init; }
    public int OverdueTasks { get; init; }

    /// <summary>Tỉ lệ hoàn thành (%), 0–100.</summary>
    public decimal CompletionRatePercent { get; init; }

    /// <summary>Số task theo trạng thái (trong cohort CreatedAtUtc).</summary>
    public IReadOnlyDictionary<TaskStatus, int> TasksByStatus { get; init; } =
        new Dictionary<TaskStatus, int>();

    /// <summary>Bar chart: số task (accepted) theo member trong cohort.</summary>
    public IReadOnlyList<MemberTaskCountVm> TasksPerMember { get; init; } = Array.Empty<MemberTaskCountVm>();

    /// <summary>Bảng hiệu suất — CompletionRate/AvgScore/Level từ MemberProfile (UC-08).</summary>
    public IReadOnlyList<MemberPerformanceRowVm> MemberPerformance { get; init; } =
        Array.Empty<MemberPerformanceRowVm>();
}

public sealed record MemberTaskCountVm(string UserId, string DisplayName, int TaskCount);

public sealed record MemberPerformanceRowVm(
    string UserId,
    string DisplayName,
    MemberLevel Level,
    decimal CompletionRate,
    decimal AvgScore,
    /// <summary>Trung bình điểm đánh giá (1–10) trong project/kỳ, từ <see cref="TaskEvaluation" />.</summary>
    decimal? AvgEvaluationScoreInProject,
    int AssignedInProjectInPeriod,
    int CompletedInProjectInPeriod);

public sealed class KpiDashboardService : IKpiDashboardService
{
    private readonly WorkFlowProDbContext _db;

    public KpiDashboardService(WorkFlowProDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ProjectListItemVm>> ListProjectsForPmAsync(
        string pmUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId &&
            m.UserId == pmUserId &&
            m.Role == WorkspaceMemberRole.PM,
            cancellationToken);
        if (!isPm)
            return Array.Empty<ProjectListItemVm>();

        return await _db.Projects
            .AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId && (p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Archived))
            .OrderBy(p => p.Name)
            .Select(p => new ProjectListItemVm(p.Id, p.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ProjectListItemVm>> ListProjectsInWorkspaceAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.Workspaces.AsNoTracking().AnyAsync(w => w.Id == workspaceId, cancellationToken);
        if (!exists)
            return Array.Empty<ProjectListItemVm>();

        return await _db.Projects
            .AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId && (p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Archived))
            .OrderBy(p => p.Name)
            .Select(p => new ProjectListItemVm(p.Id, p.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkspaceOverviewRowVm>> ListWorkspacesOverviewAsync(
        CancellationToken cancellationToken = default)
    {
        var workspaces = await _db.Workspaces.AsNoTracking()
            .OrderBy(w => w.Name)
            .Select(w => new { w.Id, w.Name })
            .ToListAsync(cancellationToken);

        if (workspaces.Count == 0)
            return Array.Empty<WorkspaceOverviewRowVm>();

        var wsIds = workspaces.Select(w => w.Id).ToList();

        var memberCounts = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => wsIds.Contains(m.WorkspaceId))
            .GroupBy(m => m.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Cnt = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Cnt, cancellationToken);

        var projectCounts = await _db.Projects.AsNoTracking()
            .Where(p => wsIds.Contains(p.WorkspaceId) &&
                        (p.Status == ProjectStatus.Active || p.Status == ProjectStatus.Archived))
            .GroupBy(p => p.WorkspaceId)
            .Select(g => new { WorkspaceId = g.Key, Cnt = g.Count() })
            .ToDictionaryAsync(x => x.WorkspaceId, x => x.Cnt, cancellationToken);

        var taskStats = await (
            from t in _db.Tasks.AsNoTracking()
            join p in _db.Projects.AsNoTracking() on t.ProjectId equals p.Id
            where wsIds.Contains(p.WorkspaceId)
            group t by p.WorkspaceId
            into g
            select new
            {
                WorkspaceId = g.Key,
                Total = g.Count(),
                Completed = g.Count(x => x.Status == TaskStatus.Done)
            }).ToDictionaryAsync(x => x.WorkspaceId, x => (x.Total, x.Completed), cancellationToken);

        var avgCompletion = await (
            from m in _db.WorkspaceMembers.AsNoTracking()
            where m.Role == WorkspaceMemberRole.Member && wsIds.Contains(m.WorkspaceId)
            join mp in _db.MemberProfiles.AsNoTracking() on m.UserId equals mp.UserId
            group mp by m.WorkspaceId
            into g
            select new
            {
                WorkspaceId = g.Key,
                Avg = g.Average(x => x.CompletionRate)
            }).ToDictionaryAsync(x => x.WorkspaceId, x => x.Avg, cancellationToken);

        return workspaces
            .Select(w =>
            {
                var ts = taskStats.GetValueOrDefault(w.Id);
                return new WorkspaceOverviewRowVm(
                    w.Id,
                    w.Name,
                    memberCounts.GetValueOrDefault(w.Id),
                    projectCounts.GetValueOrDefault(w.Id),
                    ts.Total,
                    ts.Completed,
                    avgCompletion.GetValueOrDefault(w.Id));
            })
            .ToList();
    }

    public Task<ProjectDashboardVm?> GetProjectDashboardForWorkspaceAsync(
        Guid workspaceId,
        Guid projectId,
        DateTime fromUtcInclusive,
        DateTime toUtcInclusive,
        CancellationToken cancellationToken = default) =>
        BuildProjectDashboardAsync(workspaceId, projectId, fromUtcInclusive, toUtcInclusive, cancellationToken);

    public async Task<ProjectDashboardVm?> GetProjectDashboardAsync(
        string pmUserId,
        Guid workspaceId,
        Guid projectId,
        DateTime fromUtcInclusive,
        DateTime toUtcInclusive,
        CancellationToken cancellationToken = default)
    {
        var isPm = await _db.WorkspaceMembers.AnyAsync(m =>
            m.WorkspaceId == workspaceId &&
            m.UserId == pmUserId &&
            m.Role == WorkspaceMemberRole.PM,
            cancellationToken);
        if (!isPm)
            return null;

        return await BuildProjectDashboardAsync(
            workspaceId, projectId, fromUtcInclusive, toUtcInclusive, cancellationToken);
    }

    private async Task<ProjectDashboardVm?> BuildProjectDashboardAsync(
        Guid workspaceId,
        Guid projectId,
        DateTime fromUtcInclusive,
        DateTime toUtcInclusive,
        CancellationToken cancellationToken)
    {
        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId && p.WorkspaceId == workspaceId, cancellationToken);
        if (project is null)
            return null;

        var rangeStartUtc = fromUtcInclusive;
        var rangeEndUtc = toUtcInclusive;
        if (rangeStartUtc > rangeEndUtc)
            (rangeStartUtc, rangeEndUtc) = (rangeEndUtc, rangeStartUtc);

        // Cohort: task được tạo trong khoảng (theo CreatedAtUtc).
        var tasksInPeriod = _db.Tasks.AsNoTracking()
            .Where(t =>
                t.ProjectId == projectId &&
                t.CreatedAtUtc >= rangeStartUtc &&
                t.CreatedAtUtc <= rangeEndUtc);

        var total = await tasksInPeriod.CountAsync(cancellationToken);
        var completed = await tasksInPeriod.CountAsync(t => t.Status == TaskStatus.Done, cancellationToken);

        // Quá hạn tại thời điểm cuối kỳ: có hạn, hạn trước cuối kỳ, chưa Done/Cancelled.
        var overdue = await tasksInPeriod.CountAsync(t =>
            t.DueDateUtc.HasValue &&
            t.DueDateUtc.Value < rangeEndUtc &&
            t.Status != TaskStatus.Done &&
            t.Status != TaskStatus.Cancelled,
            cancellationToken);

        var ratePct = total == 0
            ? 0m
            : Math.Round((decimal)completed / total * 100m, 2, MidpointRounding.AwayFromZero);

        var statusGroups = await tasksInPeriod
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Cnt = g.Count() })
            .ToListAsync(cancellationToken);

        var byStatus = Enum.GetValues<TaskStatus>().ToDictionary(s => s, _ => 0);
        foreach (var row in statusGroups)
            byStatus[row.Status] = row.Cnt;

        // Task theo member: assignment Accepted, task trong cohort.
        var perMemberRaw = await (
            from a in _db.TaskAssignments.AsNoTracking()
            join t in _db.Tasks.AsNoTracking() on a.TaskId equals t.Id
            join u in _db.Users.AsNoTracking() on a.AssigneeUserId equals u.Id
            where t.ProjectId == projectId &&
                  t.CreatedAtUtc >= rangeStartUtc &&
                  t.CreatedAtUtc <= rangeEndUtc &&
                  a.Status == TaskAssignmentStatus.Accepted
            group a by new { a.AssigneeUserId, u.DisplayName, u.Email, u.UserName }
            into g
            select new
            {
                g.Key.AssigneeUserId,
                Name = g.Key.DisplayName ?? g.Key.Email ?? g.Key.UserName ?? g.Key.AssigneeUserId,
                Cnt = g.Count()
            })
            .OrderByDescending(x => x.Cnt)
            .ToListAsync(cancellationToken);

        var perMember = perMemberRaw
            .Select(x => new MemberTaskCountVm(x.AssigneeUserId, x.Name, x.Cnt))
            .ToList();

        // Bảng member: workspace members (Member); CompletionRate/AvgScore/Level = MemberProfile (UC-08).
        var memberUserIds = await _db.WorkspaceMembers.AsNoTracking()
            .Where(m => m.WorkspaceId == workspaceId && m.Role == WorkspaceMemberRole.Member)
            .Select(m => m.UserId)
            .ToListAsync(cancellationToken);

        var usersDict = await _db.Users.AsNoTracking()
            .Where(u => memberUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, cancellationToken);

        var profilesDict = await _db.MemberProfiles.AsNoTracking()
            .Where(p => memberUserIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        var countsByMember = await (
            from a in _db.TaskAssignments.AsNoTracking()
            join t in _db.Tasks.AsNoTracking() on a.TaskId equals t.Id
            where t.ProjectId == projectId &&
                  t.CreatedAtUtc >= rangeStartUtc &&
                  t.CreatedAtUtc <= rangeEndUtc &&
                  a.Status == TaskAssignmentStatus.Accepted &&
                  memberUserIds.Contains(a.AssigneeUserId)
            group t by a.AssigneeUserId
            into g
            select new
            {
                UserId = g.Key,
                Assigned = g.Count(),
                Completed = g.Count(x => x.Status == TaskStatus.Done)
            }).ToDictionaryAsync(x => x.UserId, x => (x.Assigned, x.Completed), cancellationToken);

        // UC-13: điểm đánh giá trung bình trong project (theo EvaluatedAtUtc), gắn với assignee đã Accepted.
        var evalAvgByMember = await (
            from e in _db.TaskEvaluations.AsNoTracking()
            join t in _db.Tasks.AsNoTracking() on e.TaskId equals t.Id
            join a in _db.TaskAssignments.AsNoTracking() on e.TaskId equals a.TaskId
            where t.ProjectId == projectId &&
                  e.EvaluatedAtUtc >= rangeStartUtc &&
                  e.EvaluatedAtUtc <= rangeEndUtc &&
                  a.Status == TaskAssignmentStatus.Accepted &&
                  memberUserIds.Contains(a.AssigneeUserId)
            group e by a.AssigneeUserId
            into g
            select new
            {
                UserId = g.Key,
                Avg = g.Average(x => (decimal)x.Score)
            }).ToDictionaryAsync(x => x.UserId, x => x.Avg, cancellationToken);

        var performance = new List<MemberPerformanceRowVm>();
        foreach (var uid in memberUserIds)
        {
            if (!usersDict.TryGetValue(uid, out var user))
                continue;

            profilesDict.TryGetValue(uid, out var profile);
            countsByMember.TryGetValue(uid, out var ac);
            decimal? evalInProject = evalAvgByMember.TryGetValue(uid, out var ev) ? ev : null;

            performance.Add(new MemberPerformanceRowVm(
                uid,
                user.DisplayName ?? user.Email ?? user.UserName ?? uid,
                profile?.Level ?? MemberLevel.Junior,
                profile?.CompletionRate ?? 0m,
                profile?.AvgScore ?? 0m,
                evalInProject,
                ac.Assigned,
                ac.Completed));
        }

        performance = performance.OrderByDescending(p => p.AssignedInProjectInPeriod).ToList();

        return new ProjectDashboardVm
        {
            ProjectId = project.Id,
            ProjectName = project.Name,
            FromUtc = rangeStartUtc,
            ToUtc = rangeEndUtc,
            TotalTasks = total,
            CompletedTasks = completed,
            OverdueTasks = overdue,
            CompletionRatePercent = ratePct,
            TasksByStatus = byStatus,
            TasksPerMember = perMember,
            MemberPerformance = performance
        };
    }
}
