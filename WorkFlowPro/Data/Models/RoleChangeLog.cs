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
}

