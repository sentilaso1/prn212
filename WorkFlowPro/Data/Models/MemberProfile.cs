using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class MemberProfile
{
    [Key]
    public string UserId { get; set; } = default!;

    public MemberLevel Level { get; set; } = MemberLevel.Junior;
    public decimal CompletionRate { get; set; } = 0m; // 0..1
    public decimal AvgScore { get; set; } = 0m; // 1..10
    public int CurrentWorkload { get; set; } = 0;
}

