namespace WorkFlowPro.Data;

public sealed class LevelChangeLog
{
    public int Id { get; set; }
    public string TargetUserId { get; set; } = default!;
    public string ChangedByPmId { get; set; } = default!;
    public MemberLevel OldLevel { get; set; }
    public MemberLevel NewLevel { get; set; }
    public DateTime ChangedAt { get; set; }
}