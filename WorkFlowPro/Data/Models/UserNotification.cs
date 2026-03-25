using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class UserNotification
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = default!;
    public Guid? WorkspaceId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? TaskId { get; set; }

    public NotificationType Type { get; set; }
    [MaxLength(2000)]
    public string Message { get; set; } = default!;

    public bool IsRead { get; set; }

    [MaxLength(500)]
    public string? RedirectUrl { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

