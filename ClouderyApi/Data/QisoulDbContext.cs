using ClouderyApi.Models.Qisoul;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Data;

public class QisoulDbContext : DbContext
{
    public QisoulDbContext(DbContextOptions<QisoulDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<MoodRecord> MoodRecords { get; set; }
    public DbSet<Post> Posts { get; set; }
    public DbSet<Sticky> Stickies { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // 索引优化
        modelBuilder.Entity<MoodRecord>()
            .HasIndex(m => new { m.UserId, m.RecordDate });

        modelBuilder.Entity<Post>()
            .HasIndex(p => new { p.UserId, p.Category });

        modelBuilder.Entity<Sticky>()
            .HasIndex(s => s.UserId);

        // 设置默认值
        modelBuilder.Entity<MoodRecord>()
            .Property(m => m.Intensity)
            .HasDefaultValue(3);

        modelBuilder.Entity<Post>()
            .Property(p => p.Likes)
            .HasDefaultValue(0);

        modelBuilder.Entity<Post>()
            .Property(p => p.Comments)
            .HasDefaultValue(0);

        modelBuilder.Entity<Sticky>()
            .Property(s => s.Likes)
            .HasDefaultValue(0);
    }
}