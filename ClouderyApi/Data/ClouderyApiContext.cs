using ClouderyApi.Modules.Cloudery.Application;
using ClouderyApi.Modules.Cloudery.Domain;
using ClouderyApi.Modules.Zhuxs.Application;
using ClouderyApi.Modules.Zhuxs.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ClouderyApi.Data;

/// <summary>
/// Cloudery 与 Zhuxs 两个域的过渡期共享上下文：同时实现两个持久化边界接口，
/// 物理拆分（Stage 5 §5.1 第二步）前应用层已不再直接依赖本类型。
/// </summary>
public class ClouderyApiContext(DbContextOptions<ClouderyApiContext> options)
    : DbContext(options), IClouderyDbContext, IZhuxsDbContext
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new();

    public DbSet<Whitelist> ZhuxsWhitelists { get; set; } = null!;
    public DbSet<Term> ZhuxsTerms { get; set; } = null!;
    public DbSet<Application> ZhuxsApplications { get; set; } = null!;
    public DbSet<Member> ClouderyMembers { get; set; } = null!;
    public DbSet<ExamPaper> ExamPapers { get; set; } = null!;
    public DbSet<ExamResult> ExamResults { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
#pragma warning disable CS8603
        modelBuilder.Entity<Term>()
            .Property(e => e.Information)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<TermInfo>(v, JsonSerializerOptions));
#pragma warning restore CS8603

        modelBuilder.Entity<Term>()
            .Property(e => e.Files)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<TermFile>>(v, JsonSerializerOptions));

        modelBuilder.Entity<Application>()
            .Property(e => e.Sharables)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<Sharable>>(v, JsonSerializerOptions)!);

        modelBuilder.Entity<Member>()
            .Property(e => e.Socials)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<Social>>(v, JsonSerializerOptions)!);

        modelBuilder.Entity<ExamPaper>()
            .Property(e => e.Sections)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<ExamSection>>(v, JsonSerializerOptions)!);

        // 一个人的同一条记录只应存在一份：站点把本机记录键当 clientKey 上传，
        // 重复同步（多标签页、断线重试）靠这个唯一索引落在更新而不是新增上。
        // ClientKey 为 NULL 的行不受唯一索引约束（MySQL 允许多个 NULL）。
        modelBuilder.Entity<ExamResult>()
            .HasIndex(e => new { e.UserId, e.ClientKey })
            .IsUnique();

        // 云端记录列表按用户 + 存档时间倒序取
        modelBuilder.Entity<ExamResult>()
            .HasIndex(e => new { e.UserId, e.SavedAt });
    }
}