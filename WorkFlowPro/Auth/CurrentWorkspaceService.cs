using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace WorkFlowPro.Auth;

public sealed class CurrentWorkspaceService : ICurrentWorkspaceService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentWorkspaceService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? CurrentWorkspaceId
    {
        get
        {
            var http = _httpContextAccessor.HttpContext;
            if (http is null)
                return null;

            var sessionVal = http.Session.GetString(WorkspaceSessionKeys.CurrentWorkspaceId);
            if (!string.IsNullOrWhiteSpace(sessionVal) && Guid.TryParse(sessionVal, out var sessionGuid))
                return sessionGuid;

            // Ưu tiên giá trị mới nhất nếu có trùng claim (trước khi transformation dọn sạch).
            var identity = http.User?.Identity as ClaimsIdentity;
            if (identity is not null)
            {
                var lastWs = identity.FindAll("workspace_id").Select(c => c.Value).LastOrDefault()
                             ?? identity.FindAll("CurrentWorkspaceId").Select(c => c.Value).LastOrDefault();
                if (!string.IsNullOrWhiteSpace(lastWs) && Guid.TryParse(lastWs, out var lastGuid))
                    return lastGuid;
            }

            if (http.User is not { } user)
                return null;

            var claimVal =
                user.FindFirstValue("CurrentWorkspaceId")
                ?? user.FindFirstValue("workspace_id");

            if (!string.IsNullOrWhiteSpace(claimVal) && Guid.TryParse(claimVal, out var claimGuid))
                return claimGuid;

            return null;
        }
    }
}

