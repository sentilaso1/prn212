using Microsoft.AspNetCore.Http;
using WorkFlowPro.Data;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Services;

using TaskStatus = WorkFlowPro.Data.TaskStatus;

public interface ITaskService
{
    /// <summary>UC-16: Lọc & sắp xếp task cho Kanban (IQueryable + Where).</summary>
    Task<FilteredKanbanTasksResult> GetFilteredKanbanTasksAsync(
        Guid projectId,
        Guid workspaceId,
        string actorUserId,
        TaskFilterCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>UC-16: Danh sách task phẳng (Task List) với cùng bộ lọc.</summary>
    Task<FilteredTaskListResult> GetFilteredTaskListAsync(
        Guid projectId,
        Guid workspaceId,
        string actorUserId,
        TaskFilterCriteria criteria,
        CancellationToken cancellationToken = default);

    /// <summary>UC-16: Thành viên workspace cho multi-select assignee.</summary>
    Task<IReadOnlyList<WorkspaceMemberFilterOptionVm>> GetWorkspaceMemberFilterOptionsAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SuggestedAssigneeVm>> GetSuggestedAssigneesAsync(
        Guid workspaceId,
        TaskPriority priority,
        int take,
        CancellationToken cancellationToken = default);

    Task<TaskDetailVm> GetTaskDetailAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AttachmentVm>> GetTaskAttachmentsAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommentNodeVm>> GetTaskCommentsAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistoryEntryVm>> GetTaskHistoryAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<TaskEvaluationVm?> GetTaskEvaluationAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TaskEvaluationVm>> GetTaskEvaluationsAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        CancellationToken cancellationToken = default);

    Task<TaskUpdateResult> UpdateTaskAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        TaskUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<AttachmentUploadResult> AddAttachmentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        IFormFile file,
        CancellationToken cancellationToken = default);

    Task<TaskUpdateResult> DeleteAttachmentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        Guid attachmentId,
        CancellationToken cancellationToken = default);

    Task<CommentActionResult> AddCommentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        string content,
        Guid? parentCommentId,
        CancellationToken cancellationToken = default);

    Task<CommentActionResult> EditCommentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        Guid commentId,
        string content,
        CancellationToken cancellationToken = default);

    Task<CommentActionResult> DeleteCommentAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        Guid commentId,
        CancellationToken cancellationToken = default);

    Task<EvaluationUpdateResult> UpsertEvaluationAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        int score,
        string? comment,
        CancellationToken cancellationToken = default);

    Task<EvaluationUpdateResult> EvaluateTaskAsync(
        Guid taskId,
        string actorUserId,
        Guid workspaceId,
        int score,
        string? comment,
        MemberLevel? newLevel = null,
        CancellationToken cancellationToken = default);
}

public sealed record SuggestedAssigneeVm(
    string UserId,
    string DisplayName,
    MemberLevel Level,
    decimal CompletionRate,
    decimal AvgScore,
    int CurrentWorkload);

public sealed record TaskDetailVm(
    Guid TaskId,
    Guid ProjectId,
    string ProjectName,
    string Title,
    string? Description,
    TaskPriority Priority,
    DateTime? DueDateUtc,
    TaskStatus Status,
    string? AssigneeUserId,
    string AssigneeDisplayName,
    string? AssigneeAvatarUrl,
    string CreatedByUserId,
    string CreatedByDisplayName,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool IsPm,
    bool IsAssignee,
    bool CanEditCore,
    bool CanEditDescription,
    bool CanUploadAttachments,
    bool CanManageComments,
    bool ShowAcceptReject);

public sealed record AttachmentVm(
    Guid Id,
    Guid TaskId,
    string FileName,
    string FileUrl,
    long FileSizeBytes,
    string UploadedByUserId,
    string UploadedByDisplayName,
    DateTime UploadedAtUtc);

public sealed record CommentNodeVm(
    Guid Id,
    Guid TaskId,
    string UserId,
    string UserDisplayName,
    Guid? ParentCommentId,
    string Content,
    bool IsDeleted,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    IReadOnlyList<CommentNodeVm> Replies);

public sealed record HistoryEntryVm(
    Guid Id,
    Guid TaskId,
    string ActorUserId,
    string ActorDisplayName,
    string? ActorAvatarUrl,
    string Action,
    string? OldValue,
    string? NewValue,
    DateTime TimestampUtc);

public sealed record TaskEvaluationVm(
    Guid Id,
    Guid TaskId,
    string PmUserId,
    string PmDisplayName,
    string? PmAvatarUrl,
    int Score,
    string? Comment,
    DateTime EvaluatedAtUtc,
    bool IsLocked);

public sealed record TaskUpdateRequest(
    string? Title,
    string? Description,
    TaskPriority? Priority,
    DateTime? DueDateUtc,
    string? NewAssigneeUserId);

public sealed record TaskUpdateResult(
    bool Success,
    string? ErrorMessage);

public sealed record AttachmentUploadResult(
    bool Success,
    string? ErrorMessage,
    AttachmentVm? Attachment);

public sealed record CommentActionResult(
    bool Success,
    string? ErrorMessage);

public sealed record EvaluationUpdateResult(
    bool Success,
    string? ErrorMessage,
    TaskEvaluationVm? Evaluation);

