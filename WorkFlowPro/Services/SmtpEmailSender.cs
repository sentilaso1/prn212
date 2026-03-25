using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace WorkFlowPro.Services;

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _configuration;

    public SmtpEmailSender(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendEmailAsync(
        string toEmail,
        string subject,
        string bodyHtml,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toEmail))
            throw new ArgumentException("toEmail is required.", nameof(toEmail));

        // Optional "dry run" for student/dev scope.
        var dryRun = _configuration.GetValue<bool?>("Email:DryRun") ?? false;
        if (dryRun)
            return;

        var host = _configuration["Email:Smtp:Host"];
        var port = _configuration.GetValue<int?>("Email:Smtp:Port") ?? 587;
        var username = _configuration["Email:Smtp:Username"];
        var password = _configuration["Email:Smtp:Password"];
        var fromEmail = _configuration["Email:Smtp:FromEmail"];
        var fromName = _configuration["Email:Smtp:FromName"] ?? "WorkFlowPro";
        var enableSsl = _configuration.GetValue<bool?>("Email:Smtp:EnableSsl") ?? true;

        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromEmail))
            throw new InvalidOperationException(
                "SMTP email sender is not configured. Please set Email:Smtp:Host and Email:Smtp:FromEmail.");

        using var message = new MailMessage
        {
            From = new MailAddress(fromEmail, fromName),
            Subject = subject,
            Body = bodyHtml,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(toEmail));

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
        };

        if (!string.IsNullOrWhiteSpace(username) &&
            !string.IsNullOrWhiteSpace(password))
        {
            client.Credentials = new NetworkCredential(username, password);
        }

        // .NET's SmtpClient API doesn't accept cancellation tokens.
        await client.SendMailAsync(message);
    }
}

