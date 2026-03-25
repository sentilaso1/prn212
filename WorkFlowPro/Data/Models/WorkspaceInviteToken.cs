using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class WorkspaceInviteToken
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }
    [MaxLength(255)]
    public string Email { get; set; } = default!;

    [MaxLength(200)]
    public string TokenHash { get; set; } = default!;

    public WorkspaceMemberRole Role { get; set; } = WorkspaceMemberRole.Member;
    public string? SubRole { get; set; }

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}

