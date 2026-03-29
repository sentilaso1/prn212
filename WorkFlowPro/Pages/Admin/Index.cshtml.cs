using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Admin;

[Authorize(Policy = "PlatformAdmin")]
public sealed class IndexModel : PageModel
{
    private readonly IPlatformAdminService _admin;

    public IndexModel(IPlatformAdminService admin)
    {
        _admin = admin;
    }

    [TempData]
    public string? ToastMessage { get; set; }

    public bool ShowToast => !string.IsNullOrWhiteSpace(ToastMessage);

    public IReadOnlyList<PendingPmRegistrationVm> PendingRegistrations { get; private set; } =
        Array.Empty<PendingPmRegistrationVm>();

    public IReadOnlyList<WorkspaceRoleRequestListVm> PendingRoleRequests { get; private set; } =
        Array.Empty<WorkspaceRoleRequestListVm>();

    public IReadOnlyList<PendingProjectVm> PendingProjects { get; private set; } =
        Array.Empty<PendingProjectVm>();

    public IReadOnlyList<PendingLevelAdjustmentVm> PendingLevelAdjustments { get; private set; } =
        Array.Empty<PendingLevelAdjustmentVm>();

    public IReadOnlyList<AdminWorkspaceListItemVm> AllWorkspaces { get; private set; } =
        Array.Empty<AdminWorkspaceListItemVm>();

    public IReadOnlyList<AdminPmRowVm> PmsInSelectedWorkspace { get; private set; } =
        Array.Empty<AdminPmRowVm>();

    [BindProperty(SupportsGet = true)]
    public Guid? DemoteWorkspaceId { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        PendingRegistrations = await _admin.GetPendingPmRegistrationsAsync(cancellationToken);
        PendingLevelAdjustments = await _admin.GetPendingLevelAdjustmentsAsync(cancellationToken);
        PendingRoleRequests = await _admin.GetPendingWorkspaceRoleRequestsAsync(cancellationToken);
        PendingProjects = await _admin.GetPendingProjectsAsync(cancellationToken);
        AllWorkspaces = await _admin.ListAllWorkspacesAsync(cancellationToken);

        Guid? selected =
            DemoteWorkspaceId is { } d && d != Guid.Empty
                ? d
                : AllWorkspaces.FirstOrDefault()?.Id;

        if (selected is { } sid && sid != Guid.Empty)
        {
            DemoteWorkspaceId = sid;
            PmsInSelectedWorkspace = await _admin.ListPmsInWorkspaceAsync(sid, cancellationToken);
        }
    }

    public async Task<IActionResult> OnPostApproveRegistrationAsync(
        string targetUserId,
        CancellationToken cancellationToken)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var r = await _admin.ApprovePmRegistrationAsync(adminId, targetUserId, cancellationToken);
        ToastMessage = r.Success ? "Đã duyệt và tạo đơn vị cho PM." : r.ErrorMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectRegistrationAsync(
        string targetUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var r = await _admin.RejectPmRegistrationAsync(adminId, targetUserId, reason, cancellationToken);
        ToastMessage = r.Success ? "Đã từ chối đăng ký PM." : r.ErrorMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveLevelAdjustmentAsync(
        int requestId,
        CancellationToken cancellationToken)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var r = await _admin.ApproveLevelAdjustmentAsync(adminId, requestId, cancellationToken);
        ToastMessage = r.Success ? "Đã duyệt đề xuất đổi level." : r.ErrorMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectLevelAdjustmentAsync(
        int requestId,
        string reason,
        CancellationToken cancellationToken)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var r = await _admin.RejectLevelAdjustmentAsync(adminId, requestId, reason, cancellationToken);
        ToastMessage = r.Success ? "Đã từ chối đề xuất đổi level." : r.ErrorMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveRoleRequestAsync(
        int requestId,
        CancellationToken cancellationToken)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var r = await _admin.ApproveWorkspaceRoleRequestAsync(adminId, requestId, cancellationToken);
        ToastMessage = r.Success ? "Đã duyệt yêu cầu thay đổi PM." : r.ErrorMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectRoleRequestAsync(
        int requestId,
        string? adminNote,
        CancellationToken cancellationToken)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var r = await _admin.RejectWorkspaceRoleRequestAsync(adminId, requestId, adminNote, cancellationToken);
        ToastMessage = r.Success ? "Đã từ chối yêu cầu." : r.ErrorMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDemotePmDirectAsync(
        Guid workspaceId,
        string targetUserId,
        string reason,
        CancellationToken cancellationToken)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var r = await _admin.DemotePmDirectAsync(adminId, workspaceId, targetUserId, reason, cancellationToken);
        ToastMessage = r.Success ? "Đã hạ PM trực tiếp." : r.ErrorMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostApproveProjectAsync(
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var r = await _admin.ApproveProjectAsync(adminId, projectId, cancellationToken);
        ToastMessage = r.Success ? "Đã duyệt dự án." : r.ErrorMessage;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectProjectAsync(
        Guid projectId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var r = await _admin.RejectProjectAsync(adminId, projectId, reason, cancellationToken);
        ToastMessage = r.Success ? "Đã từ chối dự án." : r.ErrorMessage;
        return RedirectToPage();
    }
}
