using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
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

        public ImageController(
            IHttpClientFactory httpClientFactory, 
            AppDbContext context, 
            IWebHostEnvironment env,
            Cloudinary? cloudinary = null) // Inject Cloudinary dưới dạng optional
        {
            _httpClientFactory = httpClientFactory;
            _context = context;
            _env = env;
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
            int dailyLimit = isPro ? 30 : 10;

            // Đếm số ảnh đã xử lý hôm nay (theo giờ UTC)
            var today = DateTime.UtcNow.Date;
            var todayCount = await _context.ProcessedImages
                .CountAsync(img => img.UserId == userId && img.CreatedAt >= today);

            if (todayCount >= dailyLimit)
            {
                return StatusCode(402, new { 
                    message = $"Bạn đã dùng hết giới hạn ảnh trong ngày ({todayCount}/{dailyLimit} ảnh). Hãy nâng cấp lên tài khoản Pro (20k/tháng) để có 30 lượt dùng/ngày!",
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
                        var uploadsFolder = Path.Combine(_env.WebRootPath, "uploads");
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

                        originalUrl = $"/uploads/{originalFileName}";
                        processedUrl = $"/uploads/{processedFileName}";
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
    }
}