using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BackgroundRemovalMVP.Data;
using BackgroundRemovalMVP.Models;

namespace BackgroundRemovalMVP.Controllers
{
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        private bool IsAdminUser()
        {
            var username = User.Identity?.Name;
            return !string.IsNullOrEmpty(username) && username.ToLower() == "admin";
        }

        [HttpGet("stats")]
        public async Task<IActionResult> GetStats()
        {
            if (!IsAdminUser())
                return StatusCode(403, "Bạn không có quyền truy cập.");

            var users = await _context.Users.ToListAsync();
            var images = await _context.ProcessedImages.Include(i => i.User).ToListAsync();
            var allOrders = await _context.PaymentOrders.Include(o => o.User).ToListAsync();

            var totalImages = images.Count;
            var localImages = images.Count(i => i.Source == "Local");
            var cloudImages = images.Count(i => i.Source == "Cloud");

            // Tính tiết kiệm: mỗi ảnh Cloud API tốn khoảng $0.05, Local AI = miễn phí
            var savings = localImages * 0.05;

            // Tính tổng doanh thu từ các đơn hàng đã thanh toán
            var paidOrders = allOrders.Where(o => o.Status == "PAID").ToList();
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

            // Thống kê 7 ngày qua (số ảnh)
            var days7 = Enumerable.Range(0, 7)
                .Select(i => DateTime.UtcNow.Date.AddDays(-6 + i))
                .ToList();

            var dailyStats = days7.Select(date => new DailyStatDto
            {
                Date = date.ToString("dd/MM"),
                Count = images.Count(img => img.CreatedAt.Date == date)
            }).ToList();

            // Doanh thu 7 ngày qua
            var dailyRevenue = days7.Select(date => new DailyRevenueDto
            {
                Date = date.ToString("dd/MM"),
                Revenue = paidOrders.Where(o => o.CreatedAt.Date == date).Sum(o => (double)o.Amount)
            }).ToList();

            // Danh sách người dùng đã đăng ký
            var registeredUsers = users.Select(u => new AdminUserDto
            {
                Id = u.Id,
                Username = u.Username,
                Email = u.Email,
                IsPro = u.IsPro && u.SubscriptionExpiresAt > DateTime.UtcNow,
                SubscriptionExpiresAt = u.SubscriptionExpiresAt
            }).ToList();

            // Reviews
            var allReviews = await _context.Reviews.Include(r => r.User).ToListAsync();
            if (allReviews.Count == 0 && users.Count > 0)
            {
                var mockReviews = new List<Review>
                {
                    new Review { UserId = users[0].Id, Rating = 5, Comment = "Ứng dụng tách nền nhanh và chính xác quá!", CreatedAt = DateTime.UtcNow.AddDays(-1) },
                    new Review { UserId = users[users.Count - 1].Id, Rating = 5, Comment = "Giá nâng cấp gói Pro rẻ hơn hẳn so với các bên khác", CreatedAt = DateTime.UtcNow.AddDays(-2) },
                    new Review { UserId = users[0].Id, Rating = 4, Comment = "Chất lượng ảnh xuất ra Full HD rất sắc nét, cực kỳ hài lòng!", CreatedAt = DateTime.UtcNow.AddDays(-3) },
                    new Review { UserId = users[users.Count - 1].Id, Rating = 5, Comment = "Rất thích tính năng ghép phông nền nghệ thuật bằng AI Prompt", CreatedAt = DateTime.UtcNow.AddDays(-4) }
                };
                _context.Reviews.AddRange(mockReviews);
                await _context.SaveChangesAsync();
                allReviews = await _context.Reviews.Include(r => r.User).ToListAsync();
            }

            var totalReviews = allReviews.Count;
            var averageRating = totalReviews > 0 ? Math.Round(allReviews.Average(r => r.Rating), 1) : 5.0;

            var recentReviews = allReviews
                .OrderByDescending(r => r.CreatedAt)
                .Take(20)
                .Select(r => new ReviewDto
                {
                    Id = r.Id,
                    Username = r.User?.Username ?? "Anonymous",
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt
                }).ToList();

            var response = new AdminStatsResponse
            {
                TotalUsers = users.Count,
                TotalImages = totalImages,
                LocalImages = localImages,
                CloudImages = cloudImages,
                TotalSavings = savings,
                TotalRevenue = totalRevenue,
                TotalTransactions = allOrders.Count,
                PaidTransactions = paidOrders.Count,
                TotalReviews = totalReviews,
                AverageRating = averageRating,
                RecentActivities = recentActivities,
                DailyStats = dailyStats,
                DailyRevenue = dailyRevenue,
                RegisteredUsers = registeredUsers,
                RecentReviews = recentReviews
            };

            return Ok(response);
        }

        [HttpGet("transactions")]
        public async Task<IActionResult> GetTransactions()
        {
            if (!IsAdminUser())
                return StatusCode(403, "Bạn không có quyền truy cập.");

            var orders = await _context.PaymentOrders
                .Include(o => o.User)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new TransactionDto
                {
                    Id = o.Id,
                    OrderCode = o.OrderCode,
                    Username = o.User != null ? o.User.Username : "Unknown",
                    Email = o.User != null ? o.User.Email : "",
                    Amount = o.Amount,
                    Status = o.Status,
                    CreatedAt = o.CreatedAt
                })
                .ToListAsync();

            return Ok(orders);
        }

        [AllowAnonymous]
        [HttpGet("reviews")]
        public async Task<IActionResult> GetReviews()
        {
            var reviews = await _context.Reviews
                .Include(r => r.User)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => new ReviewDto
                {
                    Id = r.Id,
                    Username = r.User != null ? r.User.Username : "Anonymous",
                    Rating = r.Rating,
                    Comment = r.Comment,
                    CreatedAt = r.CreatedAt
                })
                .ToListAsync();

            return Ok(reviews);
        }

        [HttpGet("user/{id}/history")]
        public async Task<IActionResult> GetUserHistory(int id)
        {
            if (!IsAdminUser())
                return StatusCode(403, "Bạn không có quyền truy cập.");

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

        [HttpPut("user/{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] AdminUpdateUserDto request)
        {
            if (!IsAdminUser())
                return StatusCode(403, "Bạn không có quyền truy cập.");

            var user = await _context.Users.FindAsync(id);
            if (user == null)
                return NotFound("Người dùng không tồn tại.");

            if (!string.IsNullOrWhiteSpace(request.Email))
            {
                var emailExists = await _context.Users.AnyAsync(u => u.Id != id && !string.IsNullOrEmpty(u.Email) && u.Email.ToLower() == request.Email.ToLower());
                if (emailExists)
                    return BadRequest("Email đã được sử dụng bởi người dùng khác.");
                user.Email = request.Email;
            }

            user.IsPro = request.IsPro;
            if (request.IsPro)
            {
                user.SubscriptionExpiresAt = request.SubscriptionExpiresAt ?? DateTime.UtcNow.AddDays(30);
            }
            else
            {
                user.SubscriptionExpiresAt = null;
            }

            if (!string.IsNullOrWhiteSpace(request.NewPassword))
            {
                if (request.NewPassword.Length < 6)
                    return BadRequest("Mật khẩu mới phải có ít nhất 6 ký tự.");
                
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                var bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(request.NewPassword));
                user.PasswordHash = Convert.ToHexString(bytes);
            }

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật tài khoản người dùng thành công." });
        }

        [HttpPost("review")]
        public async Task<IActionResult> PostReview([FromBody] CreateReviewRequest request)
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Token không hợp lệ.");

            if (request.Rating < 1 || request.Rating > 5)
                return BadRequest("Đánh giá phải từ 1 đến 5 sao.");

            var review = new Review
            {
                UserId = userId,
                Rating = request.Rating,
                Comment = request.Comment ?? string.Empty,
                CreatedAt = DateTime.UtcNow
            };

            _context.Reviews.Add(review);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Gửi đánh giá thành công!" });
        }
    }
}
