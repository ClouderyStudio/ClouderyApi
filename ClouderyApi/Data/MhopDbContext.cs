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
    public DbSet<MhopBottle> MhopBottles => Set<MhopBottle>();
    public DbSet<MhopBottleMessage> MhopBottleMessages => Set<MhopBottleMessage>();

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
            e.Property(u => u.Role).HasDefaultValue(MhopUserRole.User);
            e.Property(u => u.Permissions).HasDefaultValue(string.Empty);
            e.Property(u => u.Status).HasDefaultValue(MhopUserStatus.Active);
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
            e.Property(p => p.AiFlag).HasDefaultValue(string.Empty);
            e.Property(p => p.AiReviewNote).HasDefaultValue(string.Empty);
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
            e.Property(r => r.AiFlag).HasDefaultValue(string.Empty);
            e.Property(r => r.AiReviewNote).HasDefaultValue(string.Empty);
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

        modelBuilder.Entity<MhopBottle>(e =>
        {
            e.Property(b => b.Content).HasColumnType("text");
            e.Property(b => b.Status).HasDefaultValue(MhopBottleStatus.Drifting);
            e.Property(b => b.Crisis).HasDefaultValue(false);
            e.Property(b => b.AiFlag).HasDefaultValue(string.Empty);
            e.Property(b => b.AiReviewNote).HasDefaultValue(string.Empty);
            e.Property(b => b.ReviewNote).HasDefaultValue(string.Empty);
            e.Property(b => b.ReportedCount).HasDefaultValue(0);
            e.Property(b => b.ReportedBy).HasColumnType("text");
            e.Property(b => b.ReportReason).HasDefaultValue(string.Empty);
            // 扔瓶人 / 捞瓶人只存 Id 不建外键（与 MhopPost 约定一致，避免账号删除级联）
            e.HasIndex(b => b.UserId);
            e.HasIndex(b => b.PickerUserId);
            // 捞瓶随机选取与队列筛选的核心索引
            e.HasIndex(b => new { b.Status, b.CreatedAt });
            e.HasIndex(b => b.LastMessageAt);
            e.HasMany(b => b.Messages)
                .WithOne(m => m.Bottle)
                .HasForeignKey(m => m.BottleId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<MhopBottleMessage>(e =>
        {
            e.Property(m => m.Content).HasColumnType("varchar(1000)");
            e.Property(m => m.Status).HasDefaultValue(1);
            e.Property(m => m.Crisis).HasDefaultValue(false);
            e.Property(m => m.AiFlag).HasDefaultValue(string.Empty);
            e.Property(m => m.AiReviewNote).HasDefaultValue(string.Empty);
            // after_id 增量拉取：按瓶子聚合且按 Id 顺序扫描
            e.HasIndex(m => new { m.BottleId, m.Id });
            e.HasIndex(m => m.SenderUserId);
            e.HasIndex(m => m.CreatedAt);
        });
    }
}
