using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class WorkspaceMember
{
    public Guid WorkspaceId { get; set; }
    public string UserId { get; set; } = default!;

    public WorkspaceMemberRole Role { get; set; } = WorkspaceMemberRole.Member;

    [MaxLength(100)]
    public string? SubRole { get; set; } // BA/DEV/Designer...

    public DateTime JoinedAtUtc { get; set; } = DateTime.UtcNow;
}

