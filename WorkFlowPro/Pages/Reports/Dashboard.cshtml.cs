using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Auth;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Reports;

[Authorize(Policy = "PmOrPlatformAdminForReports")]
public sealed class DashboardModel : PageModel
{
    private readonly IKpiDashboardService _kpi;
    private readonly ICurrentWorkspaceService _currentWorkspace;
    private readonly WorkFlowProDbContext _db;

    public DashboardModel(
        IKpiDashboardService kpi,
        ICurrentWorkspaceService currentWorkspace,
        WorkFlowProDbContext db)
    {
        _kpi = kpi;
        _currentWorkspace = currentWorkspace;
        _db = db;
    }

    public IReadOnlyList<ProjectListItemVm> Projects { get; private set; } = Array.Empty<ProjectListItemVm>();

    public ProjectDashboardVm? Dashboard { get; private set; }

    public string? ErrorMessage { get; private set; }

    /// <summary>Workspace đang dùng cho KPI (dùng trong link Profile / hidden field).</summary>
    public Guid? ReportWorkspaceId { get; private set; }

    public bool IsPlatformAdminViewer { get; private set; }

    public string? WorkspaceNameHint { get; private set; }

    [BindProperty(SupportsGet = true)]
    public Guid? ProjectId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    public string FromDefault { get; private set; } = string.Empty;
    public string ToDefault { get; private set; } = string.Empty;

    public string PieJson { get; private set; } = "[]";
    public string BarLabelsJson { get; private set; } = "[]";
    public string BarDataJson { get; private set; } = "[]";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            ErrorMessage = "Chưa đăng nhập.";
            return;
        }

        var workspaceId = _currentWorkspace.CurrentWorkspaceId;
        if (workspaceId is null)
        {
            ErrorMessage = "Workspace không hợp lệ.";
            return;
        }

        ReportWorkspaceId = workspaceId;
        IsPlatformAdminViewer = User.HasClaim("platform_role", "admin")
            || await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsPlatformAdmin, cancellationToken);

        var toUtc = ParseDateUtcEnd(To);
        var fromUtc = ParseDateUtcStart(From);
        if (fromUtc > toUtc)
            (fromUtc, toUtc) = (toUtc, fromUtc);

        FromDefault = fromUtc.ToString("yyyy-MM-dd");
        ToDefault = toUtc.ToString("yyyy-MM-dd");

        if (IsPlatformAdminViewer)
        {
            WorkspaceNameHint = await _db.Workspaces.AsNoTracking()
                .Where(w => w.Id == workspaceId.Value)
                .Select(w => w.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        Projects = IsPlatformAdminViewer
            ? await _kpi.ListProjectsInWorkspaceAsync(workspaceId.Value, cancellationToken)
            : await _kpi.ListProjectsForPmAsync(userId, workspaceId.Value, cancellationToken);

        if (Projects.Count == 0)
        {
            ErrorMessage = "Không có project trong workspace.";
            return;
        }

        var selectedId = ProjectId ?? Projects[0].Id;
        ProjectId = selectedId;

        Dashboard = IsPlatformAdminViewer
            ? await _kpi.GetProjectDashboardForWorkspaceAsync(
                workspaceId.Value,
                selectedId,
                fromUtc,
                toUtc,
                cancellationToken)
            : await _kpi.GetProjectDashboardAsync(
                userId,
                workspaceId.Value,
                selectedId,
                fromUtc,
                toUtc,
                cancellationToken);

        if (Dashboard is null)
        {
            ErrorMessage = "Không tải được dashboard (project không hợp lệ).";
            return;
        }

        BuildChartJson(Dashboard);
    }

    private void BuildChartJson(ProjectDashboardVm d)
    {
        var statusLabels = new Dictionary<string, string>
        {
            ["Unassigned"] = "Chưa gán",
            ["Pending"] = "Chờ nhận",
            ["ToDo"] = "To Do",
            ["InProgress"] = "Đang làm",
            ["Review"] = "Review",
            ["Done"] = "Hoàn thành",
            ["Cancelled"] = "Đã hủy"
        };

        var pie = d.TasksByStatus
            .Where(x => x.Value > 0)
            .Select(x => new { label = statusLabels.GetValueOrDefault(x.Key.ToString(), x.Key.ToString()), value = x.Value })
            .ToList();

        PieJson = JsonSerializer.Serialize(pie);

        var barLabels = d.TasksPerMember.Select(m => m.DisplayName).ToList();
        var barData = d.TasksPerMember.Select(m => m.TaskCount).ToList();
        BarLabelsJson = JsonSerializer.Serialize(barLabels);
        BarDataJson = JsonSerializer.Serialize(barData);
    }

    private static DateTime ParseDateUtcStart(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || !DateTime.TryParse(s, out var d))
            return DateTime.UtcNow.Date.AddDays(-30);
        return DateTime.SpecifyKind(new DateTime(d.Year, d.Month, d.Day, 0, 0, 0), DateTimeKind.Utc);
    }

    private static DateTime ParseDateUtcEnd(string? s)
    {
        if (string.IsNullOrWhiteSpace(s) || !DateTime.TryParse(s, out var d))
            return DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
        return DateTime.SpecifyKind(new DateTime(d.Year, d.Month, d.Day, 23, 59, 59, 999), DateTimeKind.Utc);
    }

}
