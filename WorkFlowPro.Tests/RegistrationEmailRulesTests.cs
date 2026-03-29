using WorkFlowPro.Services;

namespace WorkFlowPro.Tests;

public class RegistrationEmailRulesTests
{
    [Theory]
    [InlineData("test@gmail.com", true)]
    [InlineData("TEST@GMAIL.COM", true)]
    [InlineData("user.name@gmail.com", true)]
    [InlineData("user+tag@gmail.com", true)]
    [InlineData("user@gmail.com ", true)]
    [InlineData(" user@gmail.com", true)]
    [InlineData("notgmail.com", false)]
    [InlineData("test@yahoo.com", false)]
    [InlineData("test@outlook.com", false)]
    [InlineData("test@company.com", false)]
    [InlineData("test@gmail.com.vn", false)]
    [InlineData("test@GMAIL.COM.GG", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsGmailConsumerEmail_ReturnsExpected(string email, bool expected)
    {
        var result = RegistrationEmailRules.IsGmailConsumerEmail(email);
        Assert.Equal(expected, result);
    }
}
