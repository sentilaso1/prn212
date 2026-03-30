using Microsoft.AspNetCore.Authorization;

namespace WorkFlowPro.Auth;

/// <summary>Quản trị nền tảng: claim <c>platform_role</c> hoặc <see cref="ApplicationUser.IsPlatformAdmin" /> trong DB.</summary>
public sealed class PlatformAdminRequirement : IAuthorizationRequirement;
