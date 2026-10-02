using ClouderyApi.Modules.Zhuxs.Application;
using ClouderyApi.Modules.Zhuxs.Domain;
// 命名空间段 Application 与实体名 Application 同名，必须用别名（CS0118）
using DomainApplication = ClouderyApi.Modules.Zhuxs.Domain.Application;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ClouderyApi.Modules.Zhuxs.Infrastructure.Persistence;

/// <summary>
/// Zhuxs（竹溪）域上下文：白名单、术语与申请表。
/// JSON 列（Term.Information / Term.Files / Application.Sharables）沿用既有的转换器写法定式。
/// </summary>
public class ZhuxsContext(DbContextOptions<ZhuxsContext> options) : DbContext(options), IZhuxsDbContext
{
    /// <summary>Zhuxs 域独立的迁移历史表：3 张表早于 EF 迁移存在，用幂等 baseline 对齐。</summary>
    public const string MigrationsHistoryTableName = "__EFMigrationsHistory_Zhuxs";

    private static readonly JsonSerializerOptions JsonSerializerOptions = new();

    public DbSet<Whitelist> ZhuxsWhitelists { get; set; } = null!;
    public DbSet<Term> ZhuxsTerms { get; set; } = null!;
    public DbSet<DomainApplication> ZhuxsApplications { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Term>()
            .Property(e => e.Information)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<TermInfo>(v, JsonSerializerOptions)!);

        modelBuilder.Entity<Term>()
            .Property(e => e.Files)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<TermFile>>(v, JsonSerializerOptions)!);

        modelBuilder.Entity<DomainApplication>()
            .Property(e => e.Sharables)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<Sharable>>(v, JsonSerializerOptions)!);
    }
}
