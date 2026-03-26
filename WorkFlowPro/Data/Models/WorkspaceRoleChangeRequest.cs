using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class WorkspaceRoleChangeRequest
{
    public int Id { get; set; }

    public Guid WorkspaceId { get; set; }

    [Required, MaxLength(450)]
    public string TargetUserId { get; set; } = default!;

    [Required, MaxLength(450)]
    public string RequestedByUserId { get; set; } = default!;

    public WorkspaceRoleRequestKind Kind { get; set; }

    public WorkspaceRoleRequestStatus Status { get; set; } = WorkspaceRoleRequestStatus.Pending;

    [MaxLength(500)]
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ReviewedAtUtc { get; set; }

    [MaxLength(450)]
    public string? ReviewedByAdminId { get; set; }

    [MaxLength(500)]
    public string? AdminNote { get; set; }

    public Workspace? Workspace { get; set; }
}
