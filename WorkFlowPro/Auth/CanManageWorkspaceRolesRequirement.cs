using Microsoft.AspNetCore.Authorization;

namespace WorkFlowPro.Auth;

/// <summary>UC-09: PM trong workspace hiện tại hoặc Platform Admin.</summary>
public sealed class CanManageWorkspaceRolesRequirement : IAuthorizationRequirement
{
}
