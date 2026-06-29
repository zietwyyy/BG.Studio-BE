using System;
using System.Collections.Generic;

namespace BackgroundRemovalMVP.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string ResetPasswordToken { get; set; } = string.Empty;
        public DateTime? ResetTokenExpires { get; set; }
        public bool IsPro { get; set; } = false;
        public DateTime? SubscriptionExpiresAt { get; set; }
    }

    public class ProcessedImage
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; } // Navigation property
        public string OriginalFileName { get; set; } = string.Empty;
        public string OriginalUrl { get; set; } = string.Empty;
        public string ProcessedUrl { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty; // Lưu nguồn: Local AI hoặc Cloud API
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class PaymentOrder
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; } // Navigation property
        public long OrderCode { get; set; }
        public int Amount { get; set; }
        public string Status { get; set; } = "PENDING"; // PENDING, PAID, CANCELLED
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class Review
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public User? User { get; set; }
        public int Rating { get; set; } // 1 to 5 stars
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class UserRegisterRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string VerificationCode { get; set; } = string.Empty;
    }

    public class UserLoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class AuthResponse
    {
        public string Token { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public bool IsPro { get; set; }
        public DateTime? SubscriptionExpiresAt { get; set; }
    }

    // --- DTOs Cho Admin Dashboard ---
    public class AdminStatsResponse
    {
        public int TotalUsers { get; set; }
        public int TotalImages { get; set; }
        public int LocalImages { get; set; }
        public int CloudImages { get; set; }
        public double TotalSavings { get; set; }
        public double TotalRevenue { get; set; }
        public int TotalTransactions { get; set; }
        public int PaidTransactions { get; set; }
        public int TotalReviews { get; set; }
        public double AverageRating { get; set; }
        public List<ActivityDto> RecentActivities { get; set; } = new();
        public List<DailyStatDto> DailyStats { get; set; } = new();
        public List<DailyRevenueDto> DailyRevenue { get; set; } = new();
        public List<AdminUserDto> RegisteredUsers { get; set; } = new();
        public List<ReviewDto> RecentReviews { get; set; } = new();
    }

    public class AdminUserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsPro { get; set; }
        public DateTime? SubscriptionExpiresAt { get; set; }
    }

    public class AdminUpdateUserDto
    {
        public string Email { get; set; } = string.Empty;
        public bool IsPro { get; set; }
        public DateTime? SubscriptionExpiresAt { get; set; }
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ActivityDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string OriginalFileName { get; set; } = string.Empty;
        public string OriginalUrl { get; set; } = string.Empty;
        public string ProcessedUrl { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class DailyStatDto
    {
        public string Date { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    public class DailyRevenueDto
    {
        public string Date { get; set; } = string.Empty;
        public double Revenue { get; set; }
    }

    public class TransactionDto
    {
        public int Id { get; set; }
        public long OrderCode { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public int Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class ReviewDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class UpdateProfileRequest
    {
        public string OldPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string Token { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    public class GoogleLoginRequest
    {
        public string IdToken { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    public class GenerateImageRequest
    {
        public string Prompt { get; set; } = string.Empty;
    }

    public class CreateReviewRequest
    {
        public int Rating { get; set; }
        public string Comment { get; set; } = string.Empty;
    }
}
