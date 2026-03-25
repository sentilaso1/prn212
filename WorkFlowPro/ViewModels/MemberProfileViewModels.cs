using WorkFlowPro.Data;

namespace WorkFlowPro.ViewModels;

public sealed class MemberProfilePageVm
{
    public required string TargetUserId { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public string? AvatarUrl { get; init; }
    public string? SubRole { get; init; }
    public WorkspaceMemberRole WorkspaceRole { get; init; }
    public MemberLevel Level { get; init; }
    public decimal CompletionRate { get; init; }
    public decimal AvgScore { get; init; }
    public int CurrentWorkload { get; init; }
    public bool IsSelf { get; init; }
    public bool IsPm { get; init; }
    public bool CanEditProfile { get; init; }
    public bool CanEditLevelOrSubRole { get; init; }
    public IReadOnlyList<ProfileTaskHistoryRowVm> TaskHistory { get; init; } = Array.Empty<ProfileTaskHistoryRowVm>();
}

public sealed record ProfileTaskHistoryRowVm(
    Guid TaskId,
    string Title,
    WorkFlowPro.Data.TaskStatus Status,
    DateTime? DueDateUtc,
    int? Score);

public sealed record MemberProfileResult(bool Success, string? ErrorMessage = null);
