using WorkFlowPro.Data;

namespace WorkFlowPro.Services;

public interface IAdminAuditService
{
    Task AppendAsync(
        string actorUserId,
        AdminAuditActionType actionType,
        string targetSummary,
        string? notes = null,
        string? targetUserId = null,
        Guid? targetProjectId = null,
        Guid? workspaceId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminAuditLogRowVm>> QueryAsync(
        AdminAuditLogQuery query,
        CancellationToken cancellationToken = default);
}

public sealed record AdminAuditLogQuery(
    AdminAuditActionType? ActionType = null,
    DateTime? FromUtcInclusive = null,
    DateTime? ToUtcInclusive = null,
    string? TargetUserIdFilter = null,
    string? Keyword = null);

public sealed record AdminAuditLogRowVm(
    Guid Id,
    string ActorUserId,
    string ActorDisplayName,
    AdminAuditActionType ActionType,
    string ActionLabel,
    string TargetSummary,
    string? Notes,
    DateTime TimestampUtc);
