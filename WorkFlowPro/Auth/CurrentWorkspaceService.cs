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

            try
            {
                var sessionVal = http.Session.GetString(WorkspaceSessionKeys.CurrentWorkspaceId);
                if (!string.IsNullOrWhiteSpace(sessionVal) && Guid.TryParse(sessionVal, out var sessionGuid))
                    return sessionGuid;
            }
            catch
            {
                // Session not available (e.g. SignalR WebSocket context)
            }

            var claimVal =
                http.User.FindFirstValue("CurrentWorkspaceId")
                ?? http.User.FindFirstValue("workspace_id");

            if (!string.IsNullOrWhiteSpace(claimVal) && Guid.TryParse(claimVal, out var claimGuid))
                return claimGuid;

            return null;
        }
    }
}

