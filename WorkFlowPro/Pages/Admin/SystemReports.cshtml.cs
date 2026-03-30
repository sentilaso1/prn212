using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Admin;

[Authorize(Policy = "PlatformAdmin")]
public sealed class SystemReportsModel : PageModel
{
    private readonly IKpiDashboardService _kpi;

    public SystemReportsModel(IKpiDashboardService kpi)
    {
        _kpi = kpi;
    }

    public IReadOnlyList<WorkspaceOverviewRowVm> Workspaces { get; private set; } =
        Array.Empty<WorkspaceOverviewRowVm>();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Workspaces = await _kpi.ListWorkspacesOverviewAsync(cancellationToken);
    }
}
