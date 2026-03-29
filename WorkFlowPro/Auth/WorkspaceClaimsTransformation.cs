using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkFlowPro.Data;

namespace WorkFlowPro.Auth;

public sealed class WorkspaceClaimsTransformation : IClaimsTransformation
{
    private readonly WorkFlowProDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WorkspaceClaimsTransformation(
        WorkFlowProDbContext db,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is null || !principal.Identity.IsAuthenticated)
            return principal;

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return principal;

        var http = _httpContextAccessor.HttpContext;

        // UC-15: workspaceId ưu tiên từ query/sesion (đảm bảo bền vững qua nhiều request).
        Guid? workspaceIdFromQuery = null;
        // Nếu URL có nhiều `workspaceId` (do redirect trước đó), phải lấy giá trị “mới nhất”
        // để không bị đảo ngược thứ tự theo hướng chuyển workspace.
        var queryValues = http?.Request?.Query["workspaceId"].ToArray();
        var query = (queryValues is not null && queryValues.Length > 0) ? queryValues[^1] : null;
        if (!string.IsNullOrWhiteSpace(query) && Guid.TryParse(query, out var qid))
            workspaceIdFromQuery = qid;

        Guid? workspaceIdFromSession = null;
        var sessionVal = http?.Session?.GetString(WorkspaceSessionKeys.CurrentWorkspaceId);
        if (!string.IsNullOrWhiteSpace(sessionVal) && Guid.TryParse(sessionVal, out var sid))
            workspaceIdFromSession = sid;

        Guid? workspaceIdFromClaim = null;
        var claimVal =
            principal.FindFirstValue("CurrentWorkspaceId")
            ?? principal.FindFirstValue("workspace_id");
        if (!string.IsNullOrWhiteSpace(claimVal) && Guid.TryParse(claimVal, out var cid))
            workspaceIdFromClaim = cid;

        var isPlatformAdmin = await _db.Users.AsNoTracking()
            .AnyAsync(u => u.Id == userId && u.IsPlatformAdmin, CancellationToken.None);

        async Task<Guid?> ValidateWorkspaceContextAsync(Guid? candidate, CancellationToken cancellationToken)
        {
            if (candidate is null)
                return null;

            var isMember = await _db.WorkspaceMembers.AnyAsync(m =>
                m.UserId == userId && m.WorkspaceId == candidate.Value, cancellationToken);
            if (isMember)
                return candidate;

            // UC-09 Path C: Platform Admin drill-down workspace không cần là member.
            if (isPlatformAdmin &&
                await _db.Workspaces.AsNoTracking().AnyAsync(w => w.Id == candidate.Value, cancellationToken))
                return candidate;

            return null;
        }

        var queryValid = await ValidateWorkspaceContextAsync(workspaceIdFromQuery, CancellationToken.None);
        var sessionValid = await ValidateWorkspaceContextAsync(workspaceIdFromSession, CancellationToken.None);
        var claimValid = await ValidateWorkspaceContextAsync(workspaceIdFromClaim, CancellationToken.None);

        // Chỉ ghi cảnh báo khi nguồn đó thực sự được dùng để chọn workspace.
        // Nếu URL có ?workspaceId=... hợp lệ (vd. sau AcceptInvite), không báo lỗi chỉ vì
        // session/claim còn workspace cũ mà user không còn thuộc.
        if (http?.Session is not null)
        {
            if (workspaceIdFromQuery is not null && queryValid is null)
                http.Session.SetString(WorkspaceSessionKeys.WorkspaceSwitchError,
                    "Workspace vừa chọn không còn tồn tại/không còn quyền. Vui lòng chọn workspace khác.");
            else if (workspaceIdFromQuery is null && workspaceIdFromSession is not null && sessionValid is null &&
                     claimValid is null)
                http.Session.SetString(WorkspaceSessionKeys.WorkspaceSwitchError,
                    "Workspace hiện tại không còn hợp lệ (bạn đã bị xoá). Hệ thống sẽ chuyển sang workspace khác.");
            else if (workspaceIdFromQuery is null && workspaceIdFromSession is null &&
                     workspaceIdFromClaim is not null && claimValid is null)
                http.Session.SetString(WorkspaceSessionKeys.WorkspaceSwitchError,
                    "Workspace hiện tại không còn hợp lệ (bạn đã bị xoá). Hệ thống sẽ chuyển sang workspace khác.");
        }

        Guid? workspaceIdCandidate =
            queryValid
            ?? sessionValid
            ?? claimValid;

        // Nếu query/session/claim đều không hợp lệ: lấy workspace đầu tiên user join.
        if (workspaceIdCandidate is null)
        {
            var firstWorkspace = await _db.WorkspaceMembers
                .Where(m => m.UserId == userId)
                .OrderBy(m => m.JoinedAtUtc)
                .Select(m => (Guid?)m.WorkspaceId)
                .FirstOrDefaultAsync();

            workspaceIdCandidate = firstWorkspace;
        }

        if (workspaceIdCandidate is null)
            return principal;

        var workspaceId = workspaceIdCandidate.Value.ToString("D");

        // Persist selection to session if available.
        if (http?.Session is not null)
        {
            http.Session.SetString(WorkspaceSessionKeys.CurrentWorkspaceId, workspaceIdCandidate.Value.ToString("D"));
            // URL có workspace hợp lệ → xóa cảnh báo cũ (session/claim lỗi thời trước AcceptInvite).
            if (queryValid is not null)
                http.Session.Remove(WorkspaceSessionKeys.WorkspaceSwitchError);
        }

        if (principal.Identity is ClaimsIdentity identity)
        {
            if (!principal.HasClaim("CurrentWorkspaceId", workspaceId))
                identity.AddClaim(new Claim("CurrentWorkspaceId", workspaceId));

            // Tương thích với phần code JWT hiện có: dùng workspace_id.
            if (!principal.HasClaim("workspace_id", workspaceId))
                identity.AddClaim(new Claim("workspace_id", workspaceId));
        }

        return principal;
    }
}

