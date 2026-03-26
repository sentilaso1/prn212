using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using WorkFlowPro.Data;

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

    /// <summary>Duyệt tài khoản: user thường (Gmail) thường Approved ngay; PM chờ Admin.</summary>
    public AccountStatus AccountStatus { get; set; } = AccountStatus.Approved;

    /// <summary>Đăng ký với ý định tạo đơn vị (PM) — Admin duyệt mới tạo workspace.</summary>
    public bool AwaitingPmWorkspaceApproval { get; set; }

    /// <summary>Tên đơn vị dự kiến khi Admin duyệt PM (PendingWorkspaceName).</summary>
    [MaxLength(200)]
    public string? PendingWorkspaceName { get; set; }
}

