namespace WorkFlowPro.Services;

public static class RegistrationEmailRules
{
    /// <summary>User thường: chỉ chấp nhận đăng ký với Gmail (theo yêu cầu đồ án).</summary>
    public static bool IsGmailConsumerEmail(string email)
    {
        var n = email.Trim().ToLowerInvariant();
        return n.EndsWith("@gmail.com", StringComparison.Ordinal);
    }
}
