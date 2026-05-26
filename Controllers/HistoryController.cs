using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using BackgroundRemovalMVP.Data;
using BackgroundRemovalMVP.Models;

namespace BackgroundRemovalMVP.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize] // Chỉ cho phép người dùng đã đăng nhập truy cập
    public class HistoryController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _env;

        public HistoryController(AppDbContext context, IWebHostEnvironment env)
        {
            _context = context;
            _env = env;
        }

        [HttpGet]
        public async Task<IActionResult> GetHistory()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Token không hợp lệ.");

            var history = await _context.ProcessedImages
                .Where(img => img.UserId == userId)
                .OrderByDescending(img => img.CreatedAt)
                .ToListAsync();

            return Ok(history);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteHistoryItem(int id)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Token không hợp lệ.");

            var item = await _context.ProcessedImages.FirstOrDefaultAsync(img => img.Id == id && img.UserId == userId);
            if (item == null)
                return NotFound("Không tìm thấy ảnh này trong lịch sử của bạn.");

            try
            {
                // Xóa file ảnh gốc trên ổ đĩa
                if (!string.IsNullOrEmpty(item.OriginalUrl))
                {
                    var origPath = Path.Combine(_env.WebRootPath, item.OriginalUrl.TrimStart('/'));
                    if (System.IO.File.Exists(origPath))
                    {
                        System.IO.File.Delete(origPath);
                    }
                }

                // Xóa file ảnh đã xử lý trên ổ đĩa
                if (!string.IsNullOrEmpty(item.ProcessedUrl))
                {
                    var procPath = Path.Combine(_env.WebRootPath, item.ProcessedUrl.TrimStart('/'));
                    if (System.IO.File.Exists(procPath))
                    {
                        System.IO.File.Delete(procPath);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Lưu ý] Không thể xóa file vật lý: {ex.Message}");
            }

            // Xóa record trong DB
            _context.ProcessedImages.Remove(item);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đã xóa ảnh thành công khỏi lịch sử." });
        }
    }
}
