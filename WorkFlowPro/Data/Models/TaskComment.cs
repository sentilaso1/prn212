using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class TaskComment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskId { get; set; }
    public string UserId { get; set; } = default!;

    public Guid? ParentCommentId { get; set; }

    [MaxLength(4000)]
    public string Content { get; set; } = default!;

    public bool IsDeleted { get; set; } = false;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

