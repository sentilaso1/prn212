using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required, MaxLength(200)]
    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

