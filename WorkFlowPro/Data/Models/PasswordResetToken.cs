using System.ComponentModel.DataAnnotations;

namespace WorkFlowPro.Data;

public sealed class PasswordResetToken
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    public string UserId { get; set; } = default!;
    [MaxLength(200)]
    public string TokenHash { get; set; } = default!;

    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? UsedAtUtc { get; set; }
}

