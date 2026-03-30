using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkFlowPro.Data;
using WorkFlowPro.Services;

namespace WorkFlowPro.Pages.Admin;

[Authorize(Policy = "PlatformAdmin")]
public sealed class AuditLogModel : PageModel
{
    private readonly IAdminAuditService _audit;

    public AuditLogModel(IAdminAuditService audit)
    {
        _audit = audit;
    }

    public IReadOnlyList<AdminAuditLogRowVm> Entries { get; private set; } =
        Array.Empty<AdminAuditLogRowVm>();

    public string? ErrorMessage { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? ActionFilter { get; set; }

    /// <summary>yyyy-MM-dd (UTC start of day).</summary>
    [BindProperty(SupportsGet = true)]
    public string? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? TargetUserId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Keyword { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        AdminAuditActionType? action = null;
        if (!string.IsNullOrWhiteSpace(ActionFilter) &&
            Enum.TryParse<AdminAuditActionType>(ActionFilter, ignoreCase: true, out var parsed))
            action = parsed;

        DateTime? fromUtc = ParseDateUtcStart(FromDate);
        DateTime? toUtc = ParseDateUtcEnd(ToDate);

        try
        {
            Entries = await _audit.QueryAsync(
                new AdminAuditLogQuery(
                    action,
                    fromUtc,
                    toUtc,
                    string.IsNullOrWhiteSpace(TargetUserId) ? null : TargetUserId.Trim(),
                    string.IsNullOrWhiteSpace(Keyword) ? null : Keyword.Trim()),
                cancellationToken);
        }
        catch (Exception)
        {
            ErrorMessage = "Unable to load audit log. Please try again later.";
            Entries = Array.Empty<AdminAuditLogRowVm>();
        }
    }

    private static DateTime? ParseDateUtcStart(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            return null;
        return DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
    }

    private static DateTime? ParseDateUtcEnd(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return null;
        if (!DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var d))
            return null;
        var end = d.Date.AddDays(1).AddTicks(-1);
        return DateTime.SpecifyKind(end, DateTimeKind.Utc);
    }
}
