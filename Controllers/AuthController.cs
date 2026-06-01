using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BackgroundRemovalMVP.Data;
using BackgroundRemovalMVP.Models;
using BackgroundRemovalMVP.Services;

namespace BackgroundRemovalMVP.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly IEmailService _emailService;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Code, DateTime Expires)> _registerVerificationCodes = new();

        public AuthController(AppDbContext context, IConfiguration configuration, IEmailService emailService)
        {
            _context = context;
            _configuration = configuration;
            _emailService = emailService;
        }

        [HttpPost("send-register-code")]
        public async Task<IActionResult> SendRegisterCode([FromBody] UserRegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains("@"))
                return BadRequest("Email không hợp lệ.");

            var emailExists = await _context.Users.AnyAsync(u => !string.IsNullOrEmpty(u.Email) && u.Email.ToLower() == request.Email.ToLower());
            if (emailExists)
                return BadRequest("Email này đã được đăng ký. Vui lòng dùng email khác hoặc đăng nhập.");

            var code = new Random().Next(100000, 999999).ToString();
            _registerVerificationCodes[request.Email.ToLower()] = (code, DateTime.UtcNow.AddMinutes(5));

            var subject = "Mã xác minh đăng ký tài khoản - BG.Studio";
            var body = $"<h3>Mã xác minh đăng ký của bạn là: <strong>{code}</strong></h3><p>Mã này có hiệu lực trong 5 phút. Vui lòng không chia sẻ mã này cho người khác.</p>";

            Console.WriteLine($"[DEBUG] Mã xác minh đăng ký cho {request.Email}: {code}");

            try
            {
                await _emailService.SendEmailAsync(request.Email, subject, body);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi gửi email từ máy chủ: {ex.Message}");
            }

            return Ok(new { message = "Mã xác minh đã được gửi đến email của bạn." });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] UserRegisterRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Tên đăng nhập và mật khẩu không được trống.");

            if (request.Username.Length < 3)
                return BadRequest("Tên đăng nhập phải có ít nhất 3 ký tự.");

            if (request.Password.Length < 6)
                return BadRequest("Mật khẩu phải có ít nhất 6 ký tự.");

            if (string.IsNullOrWhiteSpace(request.Email) || !request.Email.Contains("@"))
                return BadRequest("Email không hợp lệ.");

            var emailLower = request.Email.ToLower();
            if (!_registerVerificationCodes.TryGetValue(emailLower, out var verification) || 
                verification.Code != request.VerificationCode || 
                verification.Expires < DateTime.UtcNow)
            {
                return BadRequest("Mã xác minh không chính xác hoặc đã hết hạn.");
            }

            _registerVerificationCodes.TryRemove(emailLower, out _);

            var exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower());
            if (exists)
                return BadRequest("Tên đăng nhập đã tồn tại.");

            var emailExists = await _context.Users.AnyAsync(u => !string.IsNullOrEmpty(u.Email) && u.Email.ToLower() == request.Email.ToLower());
            if (emailExists)
                return BadRequest("Email đã được sử dụng.");

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = HashPassword(request.Password)
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đăng ký tài khoản thành công." });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] UserLoginRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest("Tên đăng nhập và mật khẩu không được trống.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username.ToLower() == request.Username.ToLower());
            if (user == null || user.PasswordHash != HashPassword(request.Password))
                return Unauthorized("Tên đăng nhập hoặc mật khẩu không chính xác.");

            var token = GenerateJwtToken(user);

            return Ok(new AuthResponse
            {
                Token = token,
                Username = user.Username,
                IsPro = user.IsPro && user.SubscriptionExpiresAt > DateTime.UtcNow,
                SubscriptionExpiresAt = user.SubscriptionExpiresAt
            });
        }

        [HttpPut("profile")]
        [Microsoft.AspNetCore.Authorization.Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileRequest request)
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(userIdClaim, out int userId))
                return Unauthorized("Token không hợp lệ.");

            var user = await _context.Users.FindAsync(userId);
            if (user == null) return NotFound("Người dùng không tồn tại.");

            if (user.PasswordHash != HashPassword(request.OldPassword))
                return BadRequest("Mật khẩu cũ không chính xác.");

            if (request.NewPassword.Length < 6)
                return BadRequest("Mật khẩu mới phải có ít nhất 6 ký tự.");

            user.PasswordHash = HashPassword(request.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Cập nhật mật khẩu thành công." });
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
            if (user == null) 
                return BadRequest("Không tìm thấy tài khoản với Email này.");

            var otp = new Random().Next(100000, 999999).ToString();
            user.ResetPasswordToken = otp;
            user.ResetTokenExpires = DateTime.UtcNow.AddMinutes(15);
            await _context.SaveChangesAsync();

            var subject = "Khôi phục mật khẩu - BG.Studio";
            var body = $"<h3>Mã xác nhận khôi phục mật khẩu của bạn là: <strong>{otp}</strong></h3><p>Mã này sẽ hết hạn sau 15 phút.</p>";

            Console.WriteLine($"[DEBUG] Mã khôi phục mật khẩu cho {user.Email}: {otp}");

            try
            {
                await _emailService.SendEmailAsync(user.Email, subject, body);
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Lỗi gửi email từ máy chủ: {ex.Message}");
            }

            return Ok(new { message = "Mã khôi phục đã được gửi đến email của bạn." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.ResetPasswordToken == request.Token && u.ResetTokenExpires > DateTime.UtcNow);
            if (user == null)
                return BadRequest("Mã xác nhận không hợp lệ hoặc đã hết hạn.");

            if (request.NewPassword.Length < 6)
                return BadRequest("Mật khẩu mới phải có ít nhất 6 ký tự.");

            user.PasswordHash = HashPassword(request.NewPassword);
            user.ResetPasswordToken = string.Empty;
            user.ResetTokenExpires = null;
            await _context.SaveChangesAsync();

            return Ok(new { message = "Đặt lại mật khẩu thành công. Bạn có thể đăng nhập ngay bây giờ." });
        }

        [HttpPost("google")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequest request)
        {
            if (string.IsNullOrEmpty(request.Email))
                return BadRequest("Email không hợp lệ từ Google.");

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.ToLower());
            
            if (user == null)
            {
                string username = request.Email.Split('@')[0];
                int suffix = 1;
                string originalUsername = username;
                while (await _context.Users.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
                {
                    username = $"{originalUsername}{suffix}";
                    suffix++;
                }

                user = new User
                {
                    Username = username,
                    Email = request.Email,
                    PasswordHash = HashPassword(Guid.NewGuid().ToString()),
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }

            var token = GenerateJwtToken(user);

            return Ok(new AuthResponse
            {
                Token = token,
                Username = user.Username,
                IsPro = user.IsPro && user.SubscriptionExpiresAt > DateTime.UtcNow,
                SubscriptionExpiresAt = user.SubscriptionExpiresAt
            });
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToHexString(bytes);
        }

        private string GenerateJwtToken(User user)
        {
            var keyString = _configuration["Jwt:Key"] ?? "BackgroundRemovalMVP_SecretKey_2026_KeyForSigningJwtToken";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyString));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username)
            };

            var token = new JwtSecurityToken(
                issuer: _configuration["Jwt:Issuer"] ?? "BackgroundRemovalMVP",
                audience: _configuration["Jwt:Audience"] ?? "BackgroundRemovalMVP",
                claims: claims,
                expires: DateTime.Now.AddDays(7),
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
