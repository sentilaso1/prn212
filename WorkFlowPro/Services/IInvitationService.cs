using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public interface IInvitationService
{
    Task<InviteMembersResult> InviteMembersAsync(
        Guid workspaceId,
        string emailsRaw,
        WorkspaceMemberRole role,
        string? subRole,
        CancellationToken cancellationToken = default);

    Task<AcceptInviteResult> AcceptInviteAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<RejectInviteResult> RejectInviteAsync(
        string token,
        CancellationToken cancellationToken = default);

    Task<InviteInfoResult?> GetInviteInfoAsync(
        string token,
        CancellationToken cancellationToken = default);
}

public sealed class InviteMembersResult
{
    public required IReadOnlyList<string> Errors { get; init; }
    public bool IsDryRun { get; init; }
    public IReadOnlyList<string> DebugAcceptLinks { get; init; } = Array.Empty<string>();

    public bool Success => Errors.Count == 0;
}

public sealed class AcceptInviteResult
{
    public bool Success { get; init; }
    public Guid? WorkspaceId { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class RejectInviteResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class InviteInfoResult
{
    public required string WorkspaceName { get; init; }
    public required string Email { get; init; }
    public required WorkspaceMemberRole Role { get; init; }
    public string? SubRole { get; init; }
    public required InviteStatus Status { get; init; }
}
