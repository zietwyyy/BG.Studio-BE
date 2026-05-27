using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.Extensions.Configuration;

namespace BackgroundRemovalMVP.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string body);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var host = _configuration["SMTP:Host"] ?? "smtp.gmail.com";
            var portString = _configuration["SMTP:Port"] ?? "587";
            var port = int.Parse(portString);
            var username = _configuration["SMTP:Username"];
            var password = _configuration["SMTP:Password"];

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                Console.WriteLine("[EmailService] Cảnh báo: Chưa cấu hình SMTP Username/Password. Bỏ qua gửi email.");
                return; // Fallback khi chưa cấu hình
            }

            var email = new MimeMessage();
            email.From.Add(new MailboxAddress("BG.Studio", username));
            email.To.Add(new MailboxAddress("", toEmail));
            email.Subject = subject;

            var builder = new BodyBuilder { HtmlBody = body };
            email.Body = builder.ToMessageBody();

            using var smtp = new SmtpClient();
            await smtp.ConnectAsync(host, port, SecureSocketOptions.StartTls);
            await smtp.AuthenticateAsync(username, password);
            await smtp.SendAsync(email);
            await smtp.DisconnectAsync(true);
        }
    }
}
