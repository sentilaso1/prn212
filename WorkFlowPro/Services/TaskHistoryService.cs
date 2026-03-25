using System.Security.Claims;

using WorkFlowPro.Data;
using WorkFlowPro.Extensions;

namespace WorkFlowPro.Services;

public interface ITaskHistoryService
{
    Task LogAsync(
        Guid taskId,
        ClaimsPrincipal actor,
        string action,
        string? oldValue = null,
        string? newValue = null,
        CancellationToken cancellationToken = default);
}

public sealed class TaskHistoryService : ITaskHistoryService
{
    private readonly WorkFlowProDbContext _db;

    public TaskHistoryService(WorkFlowProDbContext db)
    {
        _db = db;
    }

    public async Task LogAsync(
        Guid taskId,
        ClaimsPrincipal actor,
        string action,
        string? oldValue = null,
        string? newValue = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new TaskHistoryEntry
        {
            TaskId = taskId,
            ActorUserId = actor.GetUserId(),
            Action = action,
            OldValue = oldValue,
            NewValue = newValue,
            TimestampUtc = DateTime.UtcNow
        };

        _db.TaskHistoryEntries.Add(entry);
        await _db.SaveChangesAsync(cancellationToken);
    }
}

