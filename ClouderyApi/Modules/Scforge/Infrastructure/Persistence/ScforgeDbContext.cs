using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ClouderyApi.Modules.Scforge.Infrastructure.Persistence;

/// <summary>
/// SCForge 域上下文：插件、版本、评论与投票。
///
/// 表名统一加 <c>scforge_</c> 前缀与其它域隔离；沿用共享的 <c>__EFMigrationsHistory</c>
/// （Cloudery / Identity / Mhop 亦如此），因此迁移 Id 必须全局唯一。
/// </summary>
public class ScforgeDbContext(DbContextOptions<ScforgeDbContext> options) : DbContext(options), IScforgeDbContext
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new();

    public DbSet<ScforgePlugin> ScforgePlugins { get; set; } = null!;
    public DbSet<ScforgeVersion> ScforgeVersions { get; set; } = null!;
    public DbSet<ScforgeComment> ScforgeComments { get; set; } = null!;
    public DbSet<ScforgeVote> ScforgeVotes { get; set; } = null!;
    public DbSet<ScforgeAdmin> ScforgeAdmins { get; set; } = null!;
    public DbSet<ScforgeGameVersion> ScforgeGameVersions { get; set; } = null!;
    public DbSet<ScforgeApiKey> ScforgeApiKeys { get; set; } = null!;
    public DbSet<ScforgeAccessGrant> ScforgeAccessGrants { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ScforgeGameVersion>(entity =>
        {
            // 版本号是校验键，必须唯一；列表按权重倒序。
            entity.Property(e => e.Version).HasMaxLength(32).IsRequired();
            entity.Property(e => e.CreatedBy).HasMaxLength(80);
            entity.HasIndex(e => e.Version).IsUnique();
            entity.HasIndex(e => e.SortOrder);
        });

        modelBuilder.Entity<ScforgePlugin>(entity =>
        {
            entity.Property(e => e.Tags).HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions) ?? new List<string>());

            entity.Property(e => e.Gallery).HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions) ?? new List<string>());

            entity.Property(e => e.Kind).HasMaxLength(16).IsRequired();

            // 访问标识是 URL 的一部分，必须唯一（并发抢注由这个索引兜底）。
            entity.HasIndex(e => e.Slug).IsUnique();

            // 目录页的三种排序各有一条覆盖索引：按下载量、按最近更新、按分类浏览。
            entity.HasIndex(e => new { e.Status, e.Downloads });

            // 插件 / 模组两块板各自排序，用「类型 + 状态 + 下载量」直接命中。
            entity.HasIndex(e => new { e.Kind, e.Status, e.Downloads });
            entity.HasIndex(e => new { e.Status, e.UpdatedAt });
            entity.HasIndex(e => new { e.Status, e.Category });
            entity.HasIndex(e => new { e.AuthorId, e.UpdatedAt });

            entity.HasMany(p => p.Versions)
                .WithOne()
                .HasForeignKey(v => v.PluginId)
                .OnDelete(DeleteBehavior.Cascade);

            // 授权名单随插件级联删除：插件没了，授权也失去意义，留着只是悬空数据。
            entity.HasMany(p => p.AccessGrants)
                .WithOne()
                .HasForeignKey(g => g.PluginId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScforgeAccessGrant>(entity =>
        {
            entity.Property(e => e.UserId).IsRequired();
            entity.Property(e => e.UserName).IsRequired();

            // 授权判定热路径：按 (插件, 用户) 查一次，必须唯一且覆盖索引。
            entity.HasIndex(e => new { e.PluginId, e.UserId }).IsUnique();

            // 名单按加入时间展示。
            entity.HasIndex(e => new { e.PluginId, e.CreatedAt });
        });

        modelBuilder.Entity<ScforgeVersion>(entity =>
        {
            entity.Property(e => e.GameVersions).HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions) ?? new List<string>());

            entity.Property(e => e.Dependencies).HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<string>>(v, JsonSerializerOptions) ?? new List<string>());

            entity.HasIndex(e => new { e.PluginId, e.PublishedAt });
            entity.HasIndex(e => e.Sha256);
        });

        modelBuilder.Entity<ScforgeComment>(entity =>
        {
            entity.HasIndex(e => new { e.PluginId, e.CreatedAt });
            entity.HasIndex(e => e.ParentId);
            entity.HasIndex(e => new { e.AuthorId, e.CreatedAt });
        });

        modelBuilder.Entity<ScforgeAdmin>(entity =>
        {
            // 一个用户最多一条管理员记录：角色与权限都挂在它上面。
            entity.HasIndex(e => e.UserId).IsUnique();

            // 后台管理员列表按角色分组展示。
            entity.HasIndex(e => e.Role);
        });

        modelBuilder.Entity<ScforgeVote>(entity =>
        {
            // 一人对同一目标只有一票：重复投票落在更新上，而不是插入第二行。
            entity.HasIndex(e => new { e.UserId, e.TargetType, e.TargetId }).IsUnique();
            entity.HasIndex(e => new { e.TargetType, e.TargetId });
        });

        modelBuilder.Entity<ScforgeApiKey>(entity =>
        {
            entity.Property(e => e.Prefix).IsRequired();
            entity.Property(e => e.KeyHash).IsRequired();
            entity.Property(e => e.UserName).IsRequired();
            entity.Property(e => e.Name).IsRequired();

            // 鉴权热路径：每次带 Key 的请求都按哈希查一行，必须唯一且覆盖索引。
            entity.HasIndex(e => e.KeyHash).IsUnique();

            // 「我的 Key」列表按归属人 + 创建时间倒序。
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });

            // 后台巡检：按失效时间筛出可清理的历史记录。
            entity.HasIndex(e => e.RevokedAt);
        });
    }
}
