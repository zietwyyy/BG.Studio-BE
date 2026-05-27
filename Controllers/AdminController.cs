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

            // Tính tổng doanh thu từ các đơn hàng đã thanh toán
            var paidOrders = await _context.PaymentOrders.Where(o => o.Status == "PAID").ToListAsync();
            var totalRevenue = paidOrders.Sum(o => (double)o.Amount);

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
                Username = u.Username,
                Email = u.Email,
                IsPro = u.IsPro && u.SubscriptionExpiresAt > DateTime.UtcNow,
                SubscriptionExpiresAt = u.SubscriptionExpiresAt
            }).ToList();

            var response = new AdminStatsResponse
            {
                TotalUsers = users.Count,
                TotalImages = totalImages,
                LocalImages = localImages,
                CloudImages = cloudImages,
                TotalSavings = savings,
                TotalRevenue = totalRevenue,
                RecentActivities = recentActivities,
                DailyStats = dailyStats,
                RegisteredUsers = registeredUsers
            };

            return Ok(response);
        }

        [HttpGet("user/{id}/history")]
        public async Task<IActionResult> GetUserHistory(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound("Người dùng không tồn tại.");

            var history = await _context.ProcessedImages
                .Where(img => img.UserId == id)
                .OrderByDescending(img => img.CreatedAt)
                .Select(i => new ActivityDto
                {
                    Id = i.Id,
                    Username = user.Username,
                    OriginalFileName = i.OriginalFileName,
                    OriginalUrl = i.OriginalUrl,
                    ProcessedUrl = i.ProcessedUrl,
                    Source = i.Source,
                    CreatedAt = i.CreatedAt
                })
                .ToListAsync();

            return Ok(history);
        }
    }
}
