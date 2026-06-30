using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using BackgroundRemovalMVP.Data;
using BackgroundRemovalMVP.Models;
using CloudinaryDotNet;
using CloudinaryDotNet.Actions;

namespace BackgroundRemovalMVP.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ImageController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;
        private readonly Cloudinary? _cloudinary;
        private readonly IConfiguration _configuration;

        public ImageController(
            IHttpClientFactory httpClientFactory, 
            AppDbContext context, 
            IWebHostEnvironment env,
            IConfiguration configuration,
            Cloudinary? cloudinary = null) // Inject Cloudinary dưới dạng optional
        {
            _httpClientFactory = httpClientFactory;
            _context = context;
            _env = env;
            _configuration = configuration;
            _cloudinary = cloudinary;
        }

        [HttpPost("remove-bg")]
        public async Task<IActionResult> RemoveBackground(IFormFile file)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized("Bạn cần đăng nhập để sử dụng tính năng tách nền ảnh.");
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return Unauthorized("Tài khoản không tồn tại.");

            // Kiểm tra trạng thái Pro
            bool isPro = user.IsPro && user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt.Value > DateTime.UtcNow;
            int dailyLimit = isPro ? 10 : 5;

            // Đếm số ảnh đã xử lý hôm nay (theo giờ UTC)
            var today = DateTime.UtcNow.Date;
            var todayCount = await _context.ProcessedImages
                .CountAsync(img => img.UserId == userId && img.CreatedAt >= today);

            if (todayCount >= dailyLimit)
            {
                return StatusCode(402, new { 
                    message = $"Bạn đã dùng hết giới hạn ảnh trong ngày ({todayCount}/{dailyLimit} ảnh). Hãy nâng cấp lên tài khoản Pro (49k/tháng) để có 10 lượt dùng/ngày (300 lượt/tháng)!",
                    limitReached = true,
                    currentUsage = todayCount,
                    limit = dailyLimit
                });
            }

            if (file == null || file.Length == 0)
                return BadRequest("File ảnh không hợp lệ.");

            byte[] processedBytes;
            string sourceUsed = "";

            // 1. Thử gọi AI Engine cục bộ (FastAPI Python - localhost:8000)
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(30);

                using var content = new MultipartFormDataContent();
                using var fileStream = file.OpenReadStream();
                using var streamContent = new StreamContent(fileStream);
                content.Add(streamContent, "file", file.FileName);

                var response = await client.PostAsync("http://127.0.0.1:8000/api/remove-bg", content);
                if (response.IsSuccessStatusCode)
                {
                    processedBytes = await response.Content.ReadAsByteArrayAsync();
                    sourceUsed = "Local AI Engine";
                }
                else
                {
                    throw new Exception($"Local engine returned status {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lưu ý] Không thể sử dụng Local AI Engine: {ex.Message}. Đang chuyển sang sử dụng remove.bg trực tuyến...");

                // 2. Dự phòng: Gọi API remove.bg trực tuyến
                try
                {
                    var client = _httpClientFactory.CreateClient();
                    client.DefaultRequestHeaders.Add("X-Api-Key", "sgmmGahbfzCBdZ26t5Uc93Ls");

                    using var content = new MultipartFormDataContent();
                    using var fileStream = file.OpenReadStream();
                    using var streamContent = new StreamContent(fileStream);

                    content.Add(streamContent, "image_file", file.FileName);
                    content.Add(new StringContent("auto"), "size");

                    var response = await client.PostAsync("https://api.remove.bg/v1.0/removebg", content);
                    if (!response.IsSuccessStatusCode)
                    {
                        var errorMsg = await response.Content.ReadAsStringAsync();
                        return StatusCode((int)response.StatusCode, $"Lỗi từ remove.bg: {errorMsg}");
                    }

                    processedBytes = await response.Content.ReadAsByteArrayAsync();
                    sourceUsed = "remove.bg API";
                }
                catch (Exception cloudEx)
                {
                    return StatusCode(500, $"Không thể kết nối đến cả Local AI Engine và remove.bg: {cloudEx.Message}");
                }
            }

            // 3. Lưu trữ nếu người dùng đã đăng nhập
            if (User.Identity?.IsAuthenticated == true)
            {
                try
                {
                    string originalUrl = "";
                    string processedUrl = "";

                    // Nếu có cấu hình Cloudinary (Dùng khi deploy)
                    if (_cloudinary != null)
                    {
                        // Upload ảnh gốc lên Cloudinary
                        using (var origStream = file.OpenReadStream())
                        {
                            var origParams = new ImageUploadParams()
                            {
                                File = new FileDescription(file.FileName, origStream),
                                Folder = "bg-remover-original"
                            };
                            var origResult = await _cloudinary.UploadAsync(origParams);
                            originalUrl = origResult.SecureUrl.ToString();
                        }

                        // Upload ảnh đã tách nền lên Cloudinary
                        using (var procStream = new MemoryStream(processedBytes))
                        {
                            var procParams = new ImageUploadParams()
                            {
                                File = new FileDescription("processed.png", procStream),
                                Folder = "bg-remover-processed"
                            };
                            var procResult = await _cloudinary.UploadAsync(procParams);
                            processedUrl = procResult.SecureUrl.ToString();
                        }
                    }
                    else // Lưu cục bộ (Dùng khi chạy local)
                    {
                        var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                        var uploadsFolder = Path.Combine(webRoot, "uploads");
                        if (!Directory.Exists(uploadsFolder))
                        {
                            Directory.CreateDirectory(uploadsFolder);
                        }

                        var uniqueId = Guid.NewGuid().ToString();
                        var originalFileName = $"{uniqueId}_orig{Path.GetExtension(file.FileName)}";
                        var processedFileName = $"{uniqueId}_proc.png";

                        var originalFilePath = Path.Combine(uploadsFolder, originalFileName);
                        var processedFilePath = Path.Combine(uploadsFolder, processedFileName);

                        using (var stream = new FileStream(originalFilePath, FileMode.Create))
                        {
                            await file.CopyToAsync(stream);
                        }

                        await System.IO.File.WriteAllBytesAsync(processedFilePath, processedBytes);

                        var scheme = Request.Headers["X-Forwarded-Proto"].ToString();
                        if (string.IsNullOrEmpty(scheme))
                        {
                            scheme = Request.Scheme;
                        }
                        var baseUrl = $"{scheme}://{Request.Host}";
                        originalUrl = $"{baseUrl}/uploads/{originalFileName}";
                        processedUrl = $"{baseUrl}/uploads/{processedFileName}";
                    }

                    // Lưu thông tin vào DB
                    var processedImage = new ProcessedImage
                    {
                        UserId = userId,
                        OriginalFileName = file.FileName,
                        OriginalUrl = originalUrl,
                        ProcessedUrl = processedUrl,
                        Source = sourceUsed,
                        CreatedAt = DateTime.UtcNow
                    };

                    _context.ProcessedImages.Add(processedImage);
                    await _context.SaveChangesAsync();
                }
                catch (Exception dbEx)
                {
                    Console.WriteLine($"[Lỗi] Không thể lưu lịch sử vào DB: {dbEx.Message}");
                }
            }

            Response.Headers.Append("X-Background-Removal-Source", sourceUsed);
            return File(processedBytes, "image/png");
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateImage([FromBody] GenerateImageRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
            {
                return Unauthorized("Bạn cần đăng nhập để sử dụng tính năng tạo ảnh bằng AI.");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Prompt))
            {
                return BadRequest("Prompt không hợp lệ.");
            }

            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return Unauthorized("Tài khoản không tồn tại.");

            // Kiểm tra trạng thái Pro
            bool isPro = user.IsPro && user.SubscriptionExpiresAt.HasValue && user.SubscriptionExpiresAt.Value > DateTime.UtcNow;
            int dailyLimit = isPro ? 10 : 5;

            // Đếm số ảnh đã xử lý hôm nay (theo giờ UTC)
            var today = DateTime.UtcNow.Date;
            var todayCount = await _context.ProcessedImages
                .CountAsync(img => img.UserId == userId && img.CreatedAt >= today);

            if (todayCount >= dailyLimit)
            {
                return StatusCode(402, new { 
                    message = $"Bạn đã dùng hết giới hạn ảnh trong ngày ({todayCount}/{dailyLimit} ảnh). Hãy nâng cấp lên tài khoản Pro (49k/tháng) để có 10 lượt dùng/ngày (300 lượt/tháng)!",
                    limitReached = true,
                    currentUsage = todayCount,
                    limit = dailyLimit
                });
            }

            byte[] generatedBytes;
            string sourceUsed = "Hugging Face AI";

            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(60);

                var token = _configuration["HuggingFace:ApiToken"];
                if (string.IsNullOrEmpty(token) || token.Contains("YOUR_"))
                {
                    token = Environment.GetEnvironmentVariable("HUGGINGFACE_API_TOKEN");
                }

                if (!string.IsNullOrEmpty(token))
                {
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }

                // Tự động dịch prompt sang tiếng Anh để Hugging Face AI (FLUX.1) hiểu và tạo ra ảnh chất lượng cao nhất giống như ChatGPT/Gemini
                var translatedPrompt = await TranslateToEnglish(request.Prompt);
                
                // Tối ưu hóa prompt để tạo phông nền chuyên nghiệp và đẹp mắt hơn (tránh AI tự vẽ thêm người lạ vào ảnh)
                var enhancedPrompt = translatedPrompt;
                enhancedPrompt = enhancedPrompt
                    .Replace("create me a picture", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("create a picture of me", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("make me a picture", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("make an image of me", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("photo of me", "", StringComparison.OrdinalIgnoreCase)
                    .Replace("i am in", "in", StringComparison.OrdinalIgnoreCase)
                    .Trim();

                // Thêm các từ khóa bổ trợ để tạo phông nền trống, có độ sâu trường ảnh (bokeh) và ánh sáng studio chuyên nghiệp
                enhancedPrompt += ", empty background, background scene, no people in the background, professional studio photography, realistic, depth of field, 8k resolution, cinematic lighting, clean backdrop";

                var payload = new { inputs = enhancedPrompt };
                
                // Sử dụng model FLUX.1-schnell siêu nhanh, chất lượng cực cao mới nhất của Black Forest Labs (Miễn phí trên Hugging Face)
                var hfUrl = "https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-schnell";
                var response = await client.PostAsJsonAsync(hfUrl, payload);

                // Nếu FLUX.1-schnell bị lỗi, fallback sang model FLUX.1-dev
                if (!response.IsSuccessStatusCode)
                {
                    var fallbackUrl = "https://router.huggingface.co/hf-inference/models/black-forest-labs/FLUX.1-dev";
                    response = await client.PostAsJsonAsync(fallbackUrl, payload);
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errText = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, $"Lỗi sinh ảnh từ Hugging Face AI (FLUX.1): {errText}");
                }

                generatedBytes = await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi kết nối đến Hugging Face AI: {ex.Message}");
            }

            string originalUrl = "AI Prompt: " + request.Prompt;
            string processedUrl = "";

            try
            {
                // Nếu có cấu hình Cloudinary (Dùng khi deploy)
                if (_cloudinary != null)
                {
                    using (var procStream = new MemoryStream(generatedBytes))
                    {
                        var procParams = new ImageUploadParams()
                        {
                            File = new FileDescription("generated.png", procStream),
                            Folder = "bg-remover-processed"
                        };
                        var procResult = await _cloudinary.UploadAsync(procParams);
                        processedUrl = procResult.SecureUrl.ToString();
                    }
                }
                else // Lưu cục bộ (Dùng khi chạy local)
                {
                    var webRoot = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    var uploadsFolder = Path.Combine(webRoot, "uploads");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    var uniqueId = Guid.NewGuid().ToString();
                    var fileName = $"{uniqueId}_generated.png";
                    var filePath = Path.Combine(uploadsFolder, fileName);

                    await System.IO.File.WriteAllBytesAsync(filePath, generatedBytes);

                    var scheme = Request.Headers["X-Forwarded-Proto"].ToString();
                    if (string.IsNullOrEmpty(scheme))
                    {
                        scheme = Request.Scheme;
                    }
                    var baseUrl = $"{scheme}://{Request.Host}";
                    processedUrl = $"{baseUrl}/uploads/{fileName}";
                }

                // Lưu thông tin vào DB để tính lượt dùng và hiển thị lịch sử
                var processedImage = new ProcessedImage
                {
                    UserId = userId,
                    OriginalFileName = request.Prompt,
                    OriginalUrl = originalUrl,
                    ProcessedUrl = processedUrl,
                    Source = sourceUsed,
                    CreatedAt = DateTime.UtcNow
                };

                _context.ProcessedImages.Add(processedImage);
                await _context.SaveChangesAsync();
            }
            catch (Exception dbEx)
            {
                Console.WriteLine($"[Lỗi] Không thể lưu lịch sử vào DB: {dbEx.Message}");
            }

            return Ok(new { url = processedUrl });
        }

        private async Task<string> TranslateToEnglish(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;
            try
            {
                using (var client = new HttpClient())
                {
                    var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=en&dt=t&q={Uri.EscapeDataString(text)}";
                    var response = await client.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var json = await response.Content.ReadAsStringAsync();
                        int firstQuoteIndex = json.IndexOf('"');
                        if (firstQuoteIndex >= 0)
                        {
                            int secondQuoteIndex = json.IndexOf('"', firstQuoteIndex + 1);
                            if (secondQuoteIndex > firstQuoteIndex)
                            {
                                return json.Substring(firstQuoteIndex + 1, secondQuoteIndex - firstQuoteIndex - 1);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lỗi dịch thuật]: {ex.Message}");
            }
            return text;
        }
    }
}