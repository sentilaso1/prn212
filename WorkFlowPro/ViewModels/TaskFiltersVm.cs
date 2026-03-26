using WorkFlowPro.Services;

namespace WorkFlowPro.ViewModels;

public sealed class TaskFiltersVm
{
    public Guid ProjectId { get; init; }

    public Guid? WorkspaceId { get; init; }

    /// <summary>kanban | list — dùng cho data attributes JS.</summary>
    public string ViewContext { get; init; } = "kanban";

    public TaskFilterCriteria Criteria { get; init; } = TaskFilterCriteria.Default();

    public IReadOnlyList<WorkspaceMemberFilterOptionVm> WorkspaceMembers { get; init; } =
        Array.Empty<WorkspaceMemberFilterOptionVm>();

    public bool IsPm { get; init; }
}
