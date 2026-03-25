using System.Security.Claims;

namespace WorkFlowPro.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static string GetUserId(this ClaimsPrincipal user)
        => user.FindFirstValue(ClaimTypes.NameIdentifier)
           ?? throw new InvalidOperationException("Missing NameIdentifier claim.");

    public static Guid GetWorkspaceId(this ClaimsPrincipal user)
        => Guid.Parse(user.FindFirstValue("workspace_id")
                      ?? throw new InvalidOperationException("Missing workspace_id claim."));
}

