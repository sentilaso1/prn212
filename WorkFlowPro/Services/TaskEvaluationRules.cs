using WorkFlowPro.Data;
using TaskStatus = WorkFlowPro.Data.TaskStatus;

namespace WorkFlowPro.Services;

/// <summary>UC-08: cửa sổ sửa đánh giá; không xóa đánh giá khi task kéo khỏi Done.</summary>
public static class TaskEvaluationRules
{
    public static readonly TimeSpan EditWindow = TimeSpan.FromHours(24);

    public static bool CanPmEditEvaluation(TaskStatus taskStatus, TaskEvaluationVm? latest, DateTime utcNow)
    {
        if (taskStatus != TaskStatus.Done)
            return false;
        if (latest is null)
            return true;
        // Chỉ dựa vào mốc thời gian — tránh khoá nhầm khi IsLocked trong DB lệch (UC-08).
        return utcNow - latest.EvaluatedAtUtc <= EditWindow;
    }

    public static bool IsEvaluationEditWindowClosed(DateTime evaluatedAtUtc, DateTime utcNow) =>
        utcNow - evaluatedAtUtc > EditWindow;
}
