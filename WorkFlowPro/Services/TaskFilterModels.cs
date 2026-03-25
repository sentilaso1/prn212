using System.Text.Json.Serialization;
using WorkFlowPro.Data;
using WorkFlowPro.ViewModels;

namespace WorkFlowPro.Services;

using TaskStatus = WorkFlowPro.Data.TaskStatus;

/// <summary>UC-16: Hạn mức ngày đến hạn (UTC).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskDueDateBucket
{
    None = 0,
    Today = 1,
    ThisWeek = 2,
    ThisMonth = 3,
    Overdue = 4
}

/// <summary>UC-16: Tiêu chí sắp xếp.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskSortOption
{
    DueDateAsc = 0,
    DueDateDesc = 1,
    PriorityAsc = 2,
    PriorityDesc = 3,
    CreatedAtAsc = 4,
    CreatedAtDesc = 5,
    TitleAsc = 6,
    TitleDesc = 7
}

/// <summary>UC-16: Bộ lọc task (lưu Session / gửi API).</summary>
public sealed class TaskFilterCriteria
{
    public List<string>? AssigneeUserIds { get; set; }

    public List<TaskStatus>? Statuses { get; set; }

    public List<TaskPriority>? Priorities { get; set; }

    public TaskDueDateBucket DueDateBucket { get; set; } = TaskDueDateBucket.None;

    public TaskSortOption Sort { get; set; } = TaskSortOption.DueDateAsc;

    /// <summary>Tìm theo tiêu đề (server-side, tùy chọn).</summary>
    public string? SearchTitle { get; set; }

    public static TaskFilterCriteria Default() => new();
}

public sealed record WorkspaceMemberFilterOptionVm(
    string UserId,
    string DisplayName,
    MemberLevel? Level);

/// <summary>Kết quả Kanban sau lọc (6 cột workflow + backlog).</summary>
public sealed class FilteredKanbanTasksResult
{
    public List<TaskCardVm> Unassigned { get; init; } = new();
    public List<TaskCardVm> Pending { get; init; } = new();
    public List<TaskCardVm> ToDo { get; init; } = new();
    public List<TaskCardVm> InProgress { get; init; } = new();
    public List<TaskCardVm> Review { get; init; } = new();
    public List<TaskCardVm> Done { get; init; } = new();
}

/// <summary>Danh sách phẳng (Task List view).</summary>
public sealed class FilteredTaskListResult
{
    public IReadOnlyList<TaskCardVm> Tasks { get; init; } = Array.Empty<TaskCardVm>();
}
