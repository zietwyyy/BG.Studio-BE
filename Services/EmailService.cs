using System.Net.Http;
using System.Text;
using System.Text.Json;
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
        private readonly HttpClient _httpClient;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
            _httpClient = new HttpClient();
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var apiKey = _configuration["Brevo:ApiKey"];
            var senderEmail = _configuration["Brevo:SenderEmail"] ?? "ngvhuy1612@gmail.com";
            var senderName = _configuration["Brevo:SenderName"] ?? "FaceIn";

            if (string.IsNullOrEmpty(apiKey) || apiKey == "YOUR_BREVO_API_KEY")
            {
                Console.WriteLine("[EmailService] Cảnh báo: Chưa cấu hình Brevo ApiKey hợp lệ. Bỏ qua gửi email.");
                return;
            }

            var requestBody = new
            {
                sender = new { name = senderName, email = senderEmail },
                to = new[] { new { email = toEmail } },
                subject = subject,
                htmlContent = body
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            
            var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
            request.Headers.Add("api-key", apiKey);
            request.Content = jsonContent;

            try
            {
                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"[EmailService] Lỗi gửi email (Brevo API): {response.StatusCode} - {error}");
                    throw new Exception($"Brevo API: {response.StatusCode} - {error}");
                }
                else
                {
                    Console.WriteLine($"[EmailService] Đã gửi HTTP API email thành công tới {toEmail}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmailService] Ngoại lệ khi gửi HTTP API email: {ex.Message}");
                throw;
            }
        }
    }
}
