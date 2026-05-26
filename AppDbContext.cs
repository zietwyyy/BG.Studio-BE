using Microsoft.EntityFrameworkCore;
using BackgroundRemovalMVP.Models;

namespace BackgroundRemovalMVP.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<User> Users => Set<User>();
        public DbSet<ProcessedImage> ProcessedImages => Set<ProcessedImage>();
        public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cấu hình chỉ mục duy nhất cho Username để không bị trùng
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();
        }
    }
}
