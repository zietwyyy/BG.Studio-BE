using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BackgroundRemovalMVP.Data;
using BackgroundRemovalMVP.Models;

namespace BackgroundRemovalMVP.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            var users = await _context.Users.ToListAsync();
            var images = await _context.ProcessedImages.Include(i => i.User).ToListAsync();

            var totalImages = images.Count;
            var localImages = images.Count(i => i.Source == "Local");
            var cloudImages = images.Count(i => i.Source == "Cloud");

            // Tính tiết kiệm: mỗi ảnh Cloud API tốn khoảng $0.05, Local AI = miễn phí
            var savings = localImages * 0.05;

            // Hoạt động gần đây (20 ảnh mới nhất)
            var recentActivities = images
                .OrderByDescending(i => i.CreatedAt)
                .Take(20)
                .Select(i => new ActivityDto
                {
                    Id = i.Id,
                    Username = i.User?.Username ?? "Unknown",
                    OriginalFileName = i.OriginalFileName,
                    OriginalUrl = i.OriginalUrl,
                    ProcessedUrl = i.ProcessedUrl,
                    Source = i.Source,
                    CreatedAt = i.CreatedAt
                }).ToList();

            // Thống kê 7 ngày qua
            var dailyStats = Enumerable.Range(0, 7)
                .Select(i => DateTime.UtcNow.Date.AddDays(-i))
                .Select(date => new DailyStatDto
                {
                    Date = date.ToString("dd/MM"),
                    Count = images.Count(img => img.CreatedAt.Date == date)
                })
                .OrderBy(d => d.Date)
                .ToList();

            // Danh sách người dùng đã đăng ký
            var registeredUsers = users.Select(u => new AdminUserDto
            {
                Id = u.Id,
                Username = u.Username
            }).ToList();

            var response = new AdminStatsResponse
            {
                TotalUsers = users.Count,
                TotalImages = totalImages,
                LocalImages = localImages,
                CloudImages = cloudImages,
                TotalSavings = savings,
                RecentActivities = recentActivities,
                DailyStats = dailyStats,
                RegisteredUsers = registeredUsers
            };

            return Ok(response);
        }
    }
}
