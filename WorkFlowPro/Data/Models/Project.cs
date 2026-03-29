using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }
    [Required, MaxLength(200)]
    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    // For consistent UX colors in Kanban labels
    [MaxLength(32)]
    public string? Color { get; set; }

    public DateTime? StartDateUtc { get; set; }
    public DateTime? EndDateUtc { get; set; }

    public ProjectStatus Status { get; set; } = ProjectStatus.PendingApproval;

    public string OwnerUserId { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

