using System;

namespace WorkFlowPro.Services;

public static class TaskHistoryHelper
{
    public static string GetHumanReadableAction(HistoryEntryVm entry)
    {
        var action = entry.Action ?? string.Empty;
        var oldValue = entry.OldValue;
        var newValue = entry.NewValue;

        return action switch
        {
            "UpdatedTitle" =>
                $"Đổi Title từ “{oldValue ?? "-"}” → “{newValue ?? "-"}”",
            "UpdatedDescription" =>
                "Đã cập nhật Description",
            "UpdatedPriority" =>
                $"Đổi Priority từ {oldValue ?? "-"} → {newValue ?? "-"}",
            "UpdatedDueDateUtc" =>
                $"Đổi DueDate từ {oldValue ?? "-"} → {newValue ?? "-"}",
            "UpdatedAssignee" =>
                $"Đổi assignee từ {oldValue ?? "-"} → {newValue ?? "-"}",
            "UpdatedStatus" =>
                $"Chuyển status từ {HumanTaskStatus(oldValue)} → {HumanTaskStatus(newValue)}",

            "Tạo task" =>
                "Tạo task",
            "Accepted task" or "Accept task" =>
                $"Chấp nhận task ({HumanTaskStatus(oldValue)} → {HumanTaskStatus(newValue)})",
            "Reject task" or "Rejected task" =>
                $"Từ chối task ({HumanTaskStatus(oldValue)} → {HumanTaskStatus(newValue)})",
            "Reject reason" =>
                $"Lý do từ chối: {newValue ?? "-"}",

            _ when action.StartsWith("Moved task from ", StringComparison.OrdinalIgnoreCase) =>
                $"Chuyển task từ {HumanTaskStatus(oldValue)} sang {HumanTaskStatus(newValue)}",
            "Moved task" =>
                $"Chuyển task từ {HumanTaskStatus(oldValue)} sang {HumanTaskStatus(newValue)}",
            _ when action.Contains("Đổi status", StringComparison.OrdinalIgnoreCase) =>
                $"Chuyển status từ {HumanTaskStatus(oldValue)} → {HumanTaskStatus(newValue)}",

            "Thêm comment" =>
                "Thêm comment",
            "Chỉnh sửa comment" =>
                "Chỉnh sửa comment",
            "Xoá comment" =>
                "Xoá comment",
            "PM xoá comment của Member" =>
                "PM xoá comment của Member",

            "Thêm attachment" =>
                "Tải lên attachment",
            "Xoá attachment" =>
                "Xoá attachment",
            "Đánh giá task" =>
                BuildEvaluationAction(newValue),
            _ when action.StartsWith("Evaluated task with score ", StringComparison.OrdinalIgnoreCase) =>
                $"Đánh giá task với điểm {action["Evaluated task with score ".Length..].Trim()}",
            _ =>
                action
        };
    }

    private static string BuildEvaluationAction(string? newValue)
    {
        // newValue format currently: "Score={score}"
        if (!string.IsNullOrWhiteSpace(newValue))
        {
            var parts = newValue.Split('=', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && parts[0].Equals("Score", StringComparison.OrdinalIgnoreCase))
            {
                return $"Evaluated task với điểm {parts[1]}";
            }
        }

        return "Đánh giá task";
    }

    public static string HumanTaskStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return "-";

        return status switch
        {
            "ToDo" => "To Do",
            "InProgress" => "In Progress",
            "Review" => "Review / Testing",
            "Done" => "Done",
            "Pending" => "Pending",
            "Unassigned" => "Unassigned",
            _ => status
        };
    }

    public static string FormatRelativeTime(DateTime timestampUtc, DateTime? nowUtc = null)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var diff = now - timestampUtc;
        if (diff < TimeSpan.Zero)
            diff = TimeSpan.Zero;

        var localTs = timestampUtc.ToLocalTime();
        var localNow = now.ToLocalTime();

        // Yesterday
        if (localTs.Date == localNow.Date.AddDays(-1))
            return $"Hôm qua lúc {localTs:HH:mm}";

        if (diff.TotalMinutes < 1)
            return "vừa xong";
        if (diff.TotalMinutes < 60)
            return $"{(int)diff.TotalMinutes} phút trước";
        if (diff.TotalHours < 24)
            return $"{(int)diff.TotalHours} giờ trước";

        return localTs.ToString("dd/MM/yyyy HH:mm");
    }
}

