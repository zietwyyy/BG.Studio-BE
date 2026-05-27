using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using BackgroundRemovalMVP.Data;
using CloudinaryDotNet;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Thêm cấu hình CORS cho FE riêng biệt
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// 1. Cấu hình linh hoạt Database: Tự động chuyển PostgreSQL (khi deploy) hoặc SQLite (khi chạy local)
var postgresUrl = builder.Configuration["DATABASE_URL"] ?? Environment.GetEnvironmentVariable("DATABASE_URL");
var defaultConn = builder.Configuration.GetConnectionString("DefaultConnection");

if (!string.IsNullOrEmpty(postgresUrl))
{
    var postgresConn = ParsePostgresUrl(postgresUrl);
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(postgresConn));
    Console.WriteLine("[Database] Đang sử dụng cơ sở dữ liệu PostgreSQL (Render/Railway).");
}
else if (!string.IsNullOrEmpty(defaultConn))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(defaultConn));
    Console.WriteLine("[Database] Đang sử dụng cơ sở dữ liệu PostgreSQL (DefaultConnection).");
}
else
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite("Data Source=BackgroundRemoval.db"));
    Console.WriteLine("[Database] Đang sử dụng cơ sở dữ liệu SQLite cục bộ (BackgroundRemoval.db).");
}

// 2. Cấu hình dịch vụ Cloudinary (nếu có thông tin cấu hình)
var cloudName = builder.Configuration["Cloudinary:CloudName"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_CLOUD_NAME");
var apiKey = builder.Configuration["Cloudinary:ApiKey"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_KEY");
var apiSecret = builder.Configuration["Cloudinary:ApiSecret"] ?? Environment.GetEnvironmentVariable("CLOUDINARY_API_SECRET");

if (!string.IsNullOrEmpty(cloudName) && !string.IsNullOrEmpty(apiKey) && !string.IsNullOrEmpty(apiSecret))
{
    var account = new Account(cloudName, apiKey, apiSecret);
    var cloudinary = new Cloudinary(account);
    builder.Services.AddSingleton(cloudinary);
    Console.WriteLine("[Cloudinary] Đã cấu hình Cloudinary thành công.");
}
else
{
    Console.WriteLine("[Cloudinary] Cảnh báo: Chưa cấu hình Cloudinary. Hệ thống sẽ lưu ảnh cục bộ tại wwwroot/uploads.");
}

// Đăng ký HttpClient để kết nối sang AI Engine ngầm
builder.Services.AddHttpClient();

// Cấu hình JWT Authentication
var keyString = builder.Configuration["Jwt:Key"] ?? "BackgroundRemovalMVP_SecretKey_2026_KeyForSigningJwtToken";
var key = Encoding.UTF8.GetBytes(keyString);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "BackgroundRemovalMVP",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "BackgroundRemovalMVP",
        IssuerSigningKey = new SymmetricSecurityKey(key)
    };
});

// Cấu hình Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BackgroundRemovalMVP API", Version = "v1" });
    
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Nhập token JWT của bạn ở đây. Ví dụ: 'eyJhbGciOi...'",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// Tự động khởi tạo database khi bắt đầu chạy ứng dụng (SQLite hoặc PostgreSQL)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Database.EnsureCreated();
        Console.WriteLine("[Database] Khởi tạo các bảng thành công.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Database] Lỗi khi tạo database: {ex.Message}");
    }
}

// Configure the HTTP request pipeline.
// Bật Swagger ở mọi môi trường (cả Production) để dễ test API
app.UseSwagger();
app.UseSwaggerUI();

// Không dùng HTTPS Redirect vì Render tự handle SSL ở reverse proxy

app.UseCors("AllowAll");

app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication(); 
app.UseAuthorization();  

app.MapControllers();

app.Run();

// Hàm chuyển đổi DATABASE_URL từ dạng postgres:// sang Connection String của C#
static string ParsePostgresUrl(string url)
{
    if (string.IsNullOrEmpty(url) || !url.StartsWith("postgres://"))
        return url;

    var uri = new Uri(url);
    var userInfo = uri.UserInfo.Split(':');
    var username = userInfo[0];
    var password = userInfo.Length > 1 ? userInfo[1] : "";
    var host = uri.Host;
    var port = uri.Port;
    var database = uri.AbsolutePath.TrimStart('/');

    return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
}