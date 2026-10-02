using ClouderyApi.Modules.Cloudery.Application;
using ClouderyApi.Modules.Cloudery.Domain;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ClouderyApi.Modules.Cloudery.Infrastructure.Persistence;

/// <summary>
/// Cloudery 域上下文：站点成员、试卷与云端成绩。
/// </summary>
public class ClouderyContext(DbContextOptions<ClouderyContext> options) : DbContext(options), IClouderyDbContext
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new();

    public DbSet<Member> ClouderyMembers { get; set; } = null!;
    public DbSet<ExamPaper> ExamPapers { get; set; } = null!;
    public DbSet<ExamResult> ExamResults { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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
