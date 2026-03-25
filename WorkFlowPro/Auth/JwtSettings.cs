namespace WorkFlowPro.Auth;

public class JwtSettings
{
    public string Issuer { get; set; } = "WorkFlowPro";
    public string Audience { get; set; } = "WorkFlowPro";
    public string Key { get; set; } = "CHANGE_ME_DEV_ONLY";

    // Minutes
    public int AccessTokenMinutes { get; set; } = 120;
}

