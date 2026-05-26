using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BackgroundRemovalMVP.Data;
using BackgroundRemovalMVP.Models;

namespace BackgroundRemovalMVP.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthController(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
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

            var exists = await _context.Users.AnyAsync(u => u.Username.ToLower() == request.Username.ToLower());
            if (exists)
                return BadRequest("Tên đăng nhập đã tồn tại.");

            var user = new User
            {
                Username = request.Username,
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
