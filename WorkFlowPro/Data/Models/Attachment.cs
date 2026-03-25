using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class Attachment
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid TaskId { get; set; }
    [MaxLength(255)]
    public string FileName { get; set; } = default!;
    [MaxLength(2000)]
    public string FileUrl { get; set; } = default!;
    public long FileSizeBytes { get; set; }
    public string UploadedByUserId { get; set; } = default!;
    public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
}

