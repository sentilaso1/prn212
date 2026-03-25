namespace WorkFlowPro.Services;

public interface IEmailSender
{
    Task SendEmailAsync(
        string toEmail,
        string subject,
        string bodyHtml,
        CancellationToken cancellationToken = default);
}

