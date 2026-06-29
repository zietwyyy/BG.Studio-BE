using System.Text;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using BackgroundRemovalMVP.Data;
using BackgroundRemovalMVP.Services;
using BackgroundRemovalMVP.Models;
using CloudinaryDotNet;

var builder = WebApplication.CreateBuilder(args);

// Thêm dịch vụ Email
builder.Services.AddScoped<IEmailService, EmailService>();

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

// Đảm bảo thư mục wwwroot và uploads tồn tại trước khi Build để ASP.NET Core nhận diện Static Files
var webRootPath = Path.Combine(builder.Environment.ContentRootPath, "wwwroot");
var uploadsPath = Path.Combine(webRootPath, "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}
builder.Environment.WebRootPath = webRootPath;

var app = builder.Build();

// Tự động khởi tạo database khi bắt đầu chạy ứng dụng (SQLite hoặc PostgreSQL)
using (var scope = app.Services.CreateScope())
{
    try
    {
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Tự động đồng bộ hóa bảng migrations history nếu database đã tồn tại (do trước đây dùng EnsureCreated)
        try
        {
            // Kiểm tra bảng Users đã tồn tại chưa
            await context.Users.AnyAsync();

            // Nếu đã tồn tại, tự động chèn lịch sử migrations cũ để tránh lỗi "table already exists"
            await context.Database.ExecuteSqlRawAsync(
                "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\" (" +
                "\"MigrationId\" TEXT NOT NULL PRIMARY KEY, " +
                "\"ProductVersion\" TEXT NOT NULL" +
                ");"
            );

            if (context.Database.IsNpgsql())
            {
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
                    "VALUES ('20260527060009_InitialSupabase', '8.0.27') ON CONFLICT DO NOTHING;"
                );
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
                    "VALUES ('20260527061939_AddEmailAndResetPassword', '8.0.27') ON CONFLICT DO NOTHING;"
                );
            }
            else
            {
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
                    "VALUES ('20260527060009_InitialSupabase', '8.0.27');"
                );
                await context.Database.ExecuteSqlRawAsync(
                    "INSERT OR IGNORE INTO \"__EFMigrationsHistory\" (\"MigrationId\", \"ProductVersion\") " +
                    "VALUES ('20260527061939_AddEmailAndResetPassword', '8.0.27');"
                );
            }
            Console.WriteLine("[Database] Khởi tạo bảng lịch sử migrations thành công.");
        }
        catch (Exception ex)
        {
            // Nếu bảng Users chưa tồn tại, đây là database trống hoàn toàn, chạy migrations như bình thường
            Console.WriteLine($"[Database Pre-migration info] Bắt đầu khởi tạo database mới hoặc cập nhật migrations. Chi tiết: {ex.Message}");
        }

        context.Database.Migrate();
        Console.WriteLine("[Database] Áp dụng các migrations database thành công.");

        // Seeding database
        if (await context.Users.CountAsync() <= 1)
        {
            Console.WriteLine("[Database] Bắt đầu gieo mầm dữ liệu mẫu (Seeding)...");
            
            // Seed Users
            var usernames = new[] { "ngvhuy1612", "hoangminh", "quynhanh", "thanhnam", "linhchi", "tiendung", "vietduc", "haiphuong", "phuongthao", "trungdung" };
            var usersList = new List<User>();
            
            using var sha256 = SHA256.Create();
            var defaultHash = Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes("123456")));

            for (int i = 0; i < usernames.Length; i++)
            {
                var isPro = i < 4; // 4 Pro users
                usersList.Add(new User
                {
                    Username = usernames[i],
                    Email = $"{usernames[i]}@gmail.com",
                    PasswordHash = defaultHash,
                    IsPro = isPro,
                    SubscriptionExpiresAt = isPro ? DateTime.UtcNow.AddDays(30) : null
                });
            }
            context.Users.AddRange(usersList);
            await context.SaveChangesAsync();

            // Fetch created users to link relationships
            var usersInDb = await context.Users.Where(u => u.Username != "admin").ToListAsync();

            // Seed Payment Orders
            var random = new Random();
            var orders = new List<PaymentOrder>();
            for (int i = 0; i < 8; i++)
            {
                var userIdx = random.Next(usersInDb.Count);
                orders.Add(new PaymentOrder
                {
                    UserId = usersInDb[userIdx].Id,
                    OrderCode = 100000 + i,
                    Amount = 20000,
                    Status = "PAID",
                    CreatedAt = DateTime.UtcNow.AddDays(-random.Next(0, 7))
                });
            }
            context.PaymentOrders.AddRange(orders);
            await context.SaveChangesAsync();

            // Seed Processed Images
            var images = new List<ProcessedImage>();
            var fileNames = new[] { "avatar.jpg", "portrait_bg.png", "family_photo.jpg", "wedding_dress.png", "business_headshot.jpg" };
            for (int i = 0; i < 40; i++)
            {
                var userIdx = random.Next(usersInDb.Count);
                var fileIdx = random.Next(fileNames.Length);
                var daysAgo = random.Next(0, 7);
                var isLocal = random.NextDouble() < 0.85; // 85% local AI
                
                images.Add(new ProcessedImage
                {
                    UserId = usersInDb[userIdx].Id,
                    OriginalFileName = fileNames[fileIdx],
                    OriginalUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?q=80&w=600",
                    ProcessedUrl = "https://images.unsplash.com/photo-1534528741775-53994a69daeb?q=80&w=600",
                    Source = isLocal ? "Local rembg" : "Cloudinary API",
                    CreatedAt = DateTime.UtcNow.AddDays(-daysAgo)
                });
            }
            context.ProcessedImages.AddRange(images);
            await context.SaveChangesAsync();

            // Seed Reviews
            var comments = new[] 
            { 
                "Tách nền cực nhanh và mượt mà, rất thích hợp cho việc làm ảnh profile nhanh.",
                "Giá nâng cấp 20k quá hời so với việc tự làm photoshop, ủng hộ nhóm phát triển!",
                "Ghép nền AI bằng prompt tạo được nhiều hình nền nghệ thuật đỉnh thật sự.",
                "Tốc độ xử lý local AI rất ấn tượng, 0đ API cost giúp tiết kiệm được nhiều tiền.",
                "Hy vọng app cập nhật thêm nhiều phông nền studio có sẵn đẹp hơn nữa."
            };
            
            var reviews = new List<Review>();
            for (int i = 0; i < comments.Length; i++)
            {
                var userIdx = Math.Min(i, usersInDb.Count - 1);
                reviews.Add(new Review
                {
                    UserId = usersInDb[userIdx].Id,
                    Rating = random.Next(4, 6), // 4-5 stars
                    Comment = comments[i],
                    CreatedAt = DateTime.UtcNow.AddDays(-random.Next(1, 5))
                });
            }
            context.Reviews.AddRange(reviews);
            await context.SaveChangesAsync();

            Console.WriteLine("[Database] Hoàn tất gieo mầm dữ liệu mẫu thành công.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"[Database] Lỗi khi tạo database hoặc seeding: {ex.Message}");
    }
}

// Configure the HTTP request pipeline.
// Bật Swagger ở mọi môi trường (cả Production) để dễ test API
app.UseSwagger();
app.UseSwaggerUI();

// Không dùng HTTPS Redirect vì Render tự handle SSL ở reverse proxy

app.UseCors("AllowAll");

app.UseDefaultFiles();

// Serve the uploads directory explicitly using PhysicalFileProvider to guarantee it works on Render/Docker
var uploadsPathForStatic = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads");
if (!Directory.Exists(uploadsPathForStatic))
{
    Directory.CreateDirectory(uploadsPathForStatic);
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(uploadsPathForStatic),
    RequestPath = "/uploads",
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Headers", "Origin, X-Requested-With, Content-Type, Accept");
    }
});

app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        ctx.Context.Response.Headers.Append("Access-Control-Allow-Headers", "Origin, X-Requested-With, Content-Type, Accept");
    }
});

app.UseAuthentication(); 
app.UseAuthorization();  

app.MapControllers();

app.Run();

// Hàm chuyển đổi DATABASE_URL từ dạng postgres:// sang Connection String của C#
static string ParsePostgresUrl(string url)
{
    if (string.IsNullOrEmpty(url) || (!url.StartsWith("postgres://") && !url.StartsWith("postgresql://")))
        return url;

    var cleanUrl = url;
    if (url.StartsWith("postgresql://"))
    {
        cleanUrl = "postgres://" + url.Substring("postgresql://".Length);
    }

    var uri = new Uri(cleanUrl);
    var userInfo = uri.UserInfo.Split(':');
    var username = userInfo[0];
    var password = userInfo.Length > 1 ? userInfo[1] : "";
    var host = uri.Host;
    var port = uri.Port;
    var database = uri.AbsolutePath.TrimStart('/');

    return $"Host={host};Port={port};Database={database};Username={username};Password={password};SSL Mode=Require;Trust Server Certificate=true";
}