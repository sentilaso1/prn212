namespace WorkFlowPro.Data;

public sealed class RoleChangeLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string ChangedByUserId { get; set; } = default!;
    public string TargetUserId { get; set; } = default!;

    public WorkspaceMemberRole OldRole { get; set; }
    public WorkspaceMemberRole NewRole { get; set; }

    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

    /// <summary>UC-13 Section 3: lý do Admin hạ PM (hoặc ghi chú tương đương).</summary>
    public string? Reason { get; set; }
}

