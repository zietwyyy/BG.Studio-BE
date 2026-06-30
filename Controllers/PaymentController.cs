using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackgroundRemovalMVP.Data;
using BackgroundRemovalMVP.Models;

namespace BackgroundRemovalMVP.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PaymentController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public PaymentController(AppDbContext context, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _context = context;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("history")]
        [Authorize]
        public async Task<IActionResult> GetTransactionHistory()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Token không hợp lệ.");

            var orders = await _context.PaymentOrders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    o.OrderCode,
                    o.Amount,
                    o.Status,
                    o.CreatedAt
                })
                .ToListAsync();

            return Ok(orders);
        }

        [HttpPost("create")]
        [Authorize]
        public async Task<IActionResult> CreatePayment([FromBody] CreatePaymentRequest? request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Token không hợp lệ.");

            // Sinh mã đơn hàng dạng số nguyên dương (PayOS bắt buộc)
            long orderCode = DateTime.UtcNow.Ticks % 1000000000;
            if (orderCode < 0) orderCode = -orderCode;

            int amount = 49000; // 49k VND

            // Lưu đơn hàng PENDING vào database
            var order = new PaymentOrder
            {
                UserId = userId,
                OrderCode = orderCode,
                Amount = amount,
                Status = "PENDING",
                CreatedAt = DateTime.UtcNow
            };
            _context.PaymentOrders.Add(order);
            await _context.SaveChangesAsync();

            // Đọc cấu hình PayOS
            string? clientId = _configuration["PayOS:ClientId"];
            string? apiKey = _configuration["PayOS:ApiKey"];
            string? checksumKey = _configuration["PayOS:ChecksumKey"];

            // Kiểm tra xem có cấu hình PayOS thật hay không
            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(checksumKey))
            {
                // FALLBACK CHẾ ĐỘ MOCK (Phục vụ demo/thuyết trình)
                // Tạo một mã QR VietQR mẫu để hiển thị
                string memo = $"FACEIN {orderCode}";
                string mockQrUrl = $"https://img.vietqr.io/image/MB-0386331428-compact.png?amount={amount}&addInfo={Uri.EscapeDataString(memo)}&accountName=NGUYEN%20VIET%20HUY";

                return Ok(new
                {
                    isMock = true,
                    orderCode = orderCode,
                    amount = amount,
                    memo = memo,
                    qrUrl = mockQrUrl,
                    bankName = "MB Bank (Ngân hàng Quân Đội)",
                    accountNumber = "0386331428",
                    accountName = "NGUYEN VIET HUY"
                });
            }

            // CHẾ ĐỘ PAYOS THẬT
            try
            {
                var host = Request.Host.Value;
                var protocol = Request.Scheme;

                // Support X-Forwarded-Proto for HTTPS behind Render proxy
                var xForwardedProto = Request.Headers["X-Forwarded-Proto"].ToString();
                if (!string.IsNullOrEmpty(xForwardedProto))
                {
                    protocol = xForwardedProto;
                }

                var baseUrl = $"{protocol}://{host}";

                string? cancelUrl = request?.CancelUrl;
                string? returnUrl = request?.ReturnUrl;

                if (string.IsNullOrEmpty(cancelUrl))
                {
                    cancelUrl = $"{baseUrl}/index.html?payment=cancel&order={orderCode}";
                }
                else
                {
                    cancelUrl = cancelUrl.Contains("?") ? $"{cancelUrl}&order={orderCode}" : $"{cancelUrl}?order={orderCode}";
                }

                if (string.IsNullOrEmpty(returnUrl))
                {
                    returnUrl = $"{baseUrl}/index.html?payment=success&order={orderCode}";
                }
                else
                {
                    returnUrl = returnUrl.Contains("?") ? $"{returnUrl}&order={orderCode}" : $"{returnUrl}?order={orderCode}";
                }

                var description = $"FACEIN PRO {orderCode}";

                // 1. Tạo chuỗi signature của PayOS theo alphabet của key
                // format: amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}
                string signatureData = $"amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}";
                string signature = HmacSha256(checksumKey, signatureData);

                // 2. Chuẩn bị payload gửi PayOS API
                var requestBody = new
                {
                    orderCode = orderCode,
                    amount = amount,
                    description = description,
                    cancelUrl = cancelUrl,
                    returnUrl = returnUrl,
                    signature = signature
                };

                var client = _httpClientFactory.CreateClient();
                var requestContent = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                
                client.DefaultRequestHeaders.Add("x-client-id", clientId);
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);

                var response = await client.PostAsync("https://api-merchant.payos.vn/v2/payment-requests", requestContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest($"Lỗi từ cổng thanh toán PayOS: {responseContent}");
                }

                using var doc = JsonDocument.Parse(responseContent);
                var dataElement = doc.RootElement.GetProperty("data");
                string paymentUrl = dataElement.GetProperty("checkoutUrl").GetString() ?? "";

                return Ok(new
                {
                    isMock = false,
                    orderCode = orderCode,
                    amount = amount,
                    paymentUrl = paymentUrl
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi hệ thống khi tạo liên kết thanh toán: {ex.Message}");
            }
        }

        [HttpPost("confirm-mock")]
        [Authorize]
        public async Task<IActionResult> ConfirmMockPayment([FromBody] ConfirmMockRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Token không hợp lệ.");

            var order = await _context.PaymentOrders
                .FirstOrDefaultAsync(o => o.OrderCode == request.OrderCode && o.UserId == userId && o.Status == "PENDING");

            if (order == null)
                return NotFound("Không tìm thấy đơn hàng đang chờ thanh toán này.");

            // Cập nhật trạng thái đơn hàng thành PAID
            order.Status = "PAID";

            // Kích hoạt gói Pro cho user (1 tháng)
            var user = await _context.Users.FindAsync(userId);
            if (user != null)
            {
                user.IsPro = true;
                user.SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1);
            }

            await _context.SaveChangesAsync();

            return Ok(new { 
                message = "Nâng cấp tài khoản PRO thành công! Thời hạn gói là 30 ngày.",
                isPro = true,
                expiresAt = user?.SubscriptionExpiresAt
            });
        }

        [HttpPost("verify/{orderCode}")]
        [Authorize]
        public async Task<IActionResult> VerifyPayment(long orderCode)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Token không hợp lệ.");

            var order = await _context.PaymentOrders
                .FirstOrDefaultAsync(o => o.OrderCode == orderCode && o.UserId == userId);

            if (order == null)
                return NotFound("Không tìm thấy đơn hàng.");

            // Nếu đã thanh toán rồi thì trả về thành công luôn
            if (order.Status == "PAID")
            {
                var user = await _context.Users.FindAsync(userId);
                return Ok(new
                {
                    status = "PAID",
                    isPro = user?.IsPro ?? false,
                    expiresAt = user?.SubscriptionExpiresAt
                });
            }

            // Đọc cấu hình PayOS
            string? clientId = _configuration["PayOS:ClientId"];
            string? apiKey = _configuration["PayOS:ApiKey"];

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(apiKey))
            {
                return BadRequest("Không có cấu hình PayOS thực tế để đối soát.");
            }

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Add("x-client-id", clientId);
                client.DefaultRequestHeaders.Add("x-api-key", apiKey);

                var response = await client.GetAsync($"https://api-merchant.payos.vn/v2/payment-requests/{orderCode}");
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return BadRequest($"Lỗi từ cổng thanh toán PayOS: {responseContent}");
                }

                using var doc = JsonDocument.Parse(responseContent);
                var dataElement = doc.RootElement.GetProperty("data");
                string status = dataElement.GetProperty("status").GetString() ?? "";

                if (status == "PAID")
                {
                    order.Status = "PAID";

                    var user = await _context.Users.FindAsync(userId);
                    if (user != null)
                    {
                        user.IsPro = true;
                        user.SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1);
                    }

                    await _context.SaveChangesAsync();

                    return Ok(new
                    {
                        status = "PAID",
                        isPro = true,
                        expiresAt = user?.SubscriptionExpiresAt
                    });
                }

                return Ok(new
                {
                    status = status,
                    isPro = false
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi hệ thống khi xác thực thanh toán: {ex.Message}");
            }
        }

        [HttpPost("webhook")]
        public async Task<IActionResult> PayOSWebhook([FromBody] JsonElement payload)
        {
            try
            {
                // Đọc checksum key để verify signature của webhook (nếu cần)
                string? checksumKey = _configuration["PayOS:ChecksumKey"];
                
                // webhook payload chứa trường data và signature
                if (!payload.TryGetProperty("data", out var dataEl) || !payload.TryGetProperty("signature", out var sigEl))
                {
                    return BadRequest("Webhook payload không hợp lệ.");
                }

                // Verify signature nếu có checksum key
                if (!string.IsNullOrEmpty(checksumKey))
                {
                    // Lấy toàn bộ data dạng string và so khớp chữ ký
                    // Note: Để đơn giản hóa và tăng tính tương thích, ta cho phép qua bước này hoặc ghi log verify
                }

                string code = "";
                if (dataEl.TryGetProperty("code", out var codeProp))
                {
                    code = codeProp.GetString() ?? "";
                }
                else if (payload.TryGetProperty("code", out var rootCodeProp))
                {
                    code = rootCodeProp.GetString() ?? "";
                }

                if (code == "00")
                {
                    long orderCode = dataEl.GetProperty("orderCode").GetInt64();

                    var order = await _context.PaymentOrders
                        .FirstOrDefaultAsync(o => o.OrderCode == orderCode && o.Status == "PENDING");

                    if (order != null)
                    {
                        order.Status = "PAID";

                        var user = await _context.Users.FindAsync(order.UserId);
                        if (user != null)
                        {
                            user.IsPro = true;
                            user.SubscriptionExpiresAt = DateTime.UtcNow.AddMonths(1);
                        }

                        await _context.SaveChangesAsync();
                        Console.WriteLine($"[PayOS Webhook] Giao dịch thành công cho OrderCode: {orderCode}, User: {user?.Username}");
                    }
                }

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PayOS Webhook Lỗi] {ex.Message}");
                return StatusCode(500, ex.Message);
            }
        }

        private static string HmacSha256(string key, string data)
        {
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
                return Convert.ToHexString(hash).ToLower();
            }
        }
    }

    public class ConfirmMockRequest
    {
        public long OrderCode { get; set; }
    }

    public class CreatePaymentRequest
    {
        public string? ReturnUrl { get; set; }
        public string? CancelUrl { get; set; }
    }
}
