using Microsoft.AspNetCore.Identity;

namespace WorkFlowPro.Auth;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool IsPlatformAdmin { get; set; }
}

