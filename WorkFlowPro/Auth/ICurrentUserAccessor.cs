namespace WorkFlowPro.Auth;

/// <summary>User hiện tại (claims) — dùng cho global query filter UC-11.</summary>
public interface ICurrentUserAccessor
{
    string? UserId { get; }
}
