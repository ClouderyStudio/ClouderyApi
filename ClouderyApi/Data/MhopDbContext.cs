using ClouderyApi.Models.Mhop;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Data;

/// <summary>
/// MHOP 公益心理辅助平台数据上下文（从 Python FastAPI + SQLAlchemy 后端迁移）。
/// 使用与 ClouderyApi 相同的 MySQL 连接串，表名统一加 mhop_ 前缀以隔离域。
/// </summary>
public class MhopDbContext(DbContextOptions<MhopDbContext> options) : DbContext(options)
{
    public DbSet<MhopUser> MhopUsers => Set<MhopUser>();
    public DbSet<MhopPost> MhopPosts => Set<MhopPost>();
    public DbSet<MhopReply> MhopReplies => Set<MhopReply>();
    public DbSet<MhopLike> MhopLikes => Set<MhopLike>();
    public DbSet<MhopAssessment> MhopAssessments => Set<MhopAssessment>();
    public DbSet<MhopAiLog> MhopAiLogs => Set<MhopAiLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MhopUser>(e =>
        {
            e.HasIndex(u => u.Username).IsUnique();
            // 邮箱 / 手机号可为空；MySQL 唯一索引允许多个 NULL，与 Python 版语义一致
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.Phone).IsUnique();
            // Casdoor 统一身份：一个 Casdoor 账号只绑定一个 MHOP 账号（本地账号可为 NULL）
            e.HasIndex(u => u.CasdoorId).IsUnique();
            e.Property(u => u.Role).HasDefaultValue("user");
            e.Property(u => u.Status).HasDefaultValue("active");
            e.Property(u => u.Avatar).HasDefaultValue(string.Empty);
            e.Property(u => u.Badge).HasDefaultValue(string.Empty);
        });

        modelBuilder.Entity<MhopPost>(e =>
        {
            e.Property(p => p.Content).HasColumnType("text");
            e.Property(p => p.Images).HasColumnType("text");
            e.Property(p => p.Status).HasDefaultValue(0);
            e.Property(p => p.Crisis).HasDefaultValue(false);
            e.Property(p => p.ViewCount).HasDefaultValue(0);
            e.Property(p => p.Board).HasDefaultValue("mood");
            e.HasIndex(p => p.UserId);
            e.HasIndex(p => p.Board);
            e.HasIndex(p => p.Status);
            e.HasIndex(p => p.CreatedAt);
        });

        modelBuilder.Entity<MhopReply>(e =>
        {
            e.Property(r => r.Content).HasColumnType("text");
            e.Property(r => r.Images).HasColumnType("text");
            e.Property(r => r.Status).HasDefaultValue(0);
            e.Property(r => r.IsAi).HasDefaultValue(false);
            e.Property(r => r.Crisis).HasDefaultValue(false);
            e.Property(r => r.Recalled).HasDefaultValue(false);
            e.HasIndex(r => r.PostId);
            e.HasIndex(r => r.Status);
            e.HasIndex(r => r.Recalled);
            e.HasOne(r => r.Post)
                .WithMany(p => p.Replies)
                .HasForeignKey(r => r.PostId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MhopLike>(e =>
        {
            e.HasIndex(l => new { l.UserId, l.TargetType, l.TargetId }).IsUnique();
            e.HasIndex(l => new { l.TargetType, l.TargetId });
            e.HasIndex(l => l.UserId);
        });

        modelBuilder.Entity<MhopAssessment>(e =>
        {
            e.Property(a => a.InputData).HasColumnType("text");
            e.Property(a => a.AiResult).HasColumnType("text");
            e.HasIndex(a => a.UserId);
        });

        modelBuilder.Entity<MhopAiLog>(e =>
        {
            e.Property(l => l.Prompt).HasColumnType("text");
            e.Property(l => l.Response).HasColumnType("text");
            e.HasIndex(l => l.UserId);
            e.HasIndex(l => l.SessionId);
            e.HasIndex(l => l.ReplyId);
        });
    }
}
