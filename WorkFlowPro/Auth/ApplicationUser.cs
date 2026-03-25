using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace WorkFlowPro.Auth;

public class ApplicationUser : IdentityUser
{
    public string? DisplayName { get; set; }
    
    // UC-01 yêu cầu field FullName; tuy nhiên DB hiện đang lưu ở cột DisplayName.
    // NotMapped để tránh phát sinh migration cột mới.
    [NotMapped]
    public string? FullName
    {
        get => DisplayName;
        set => DisplayName = value;
    }

    public string? AvatarUrl { get; set; }
    public bool IsPlatformAdmin { get; set; }
}

