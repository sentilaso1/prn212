using Microsoft.AspNetCore.Authorization;

namespace WorkFlowPro.Auth;

/// <summary>UC-09 Path C: PM trong workspace hiện tại hoặc Platform Admin với workspace hợp lệ (đọc KPI).</summary>
public sealed class PmOrPlatformAdminForReportsRequirement : IAuthorizationRequirement;
