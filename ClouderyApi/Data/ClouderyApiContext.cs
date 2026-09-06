using ClouderyApi.Models.Cloudery;
using ClouderyApi.Models.Zhuxs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ClouderyApi.Data;

public class ClouderyApiContext(DbContextOptions<ClouderyApiContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new();

    public DbSet<Whitelist> ZhuxsWhitelists { get; set; } = null!;
    public DbSet<Term> ZhuxsTerms { get; set; } = null!;
    public DbSet<Application> ZhuxsApplications { get; set; } = null!;
    public DbSet<Member> ClouderyMembers { get; set; } = null!;
    public DbSet<ExamPaper> ExamPapers { get; set; } = null!;

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
                v => JsonSerializer.Deserialize<List<Sharable>>(v, JsonSerializerOptions));

        modelBuilder.Entity<Member>()
            .Property(e => e.Socials)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<Social>>(v, JsonSerializerOptions));

        modelBuilder.Entity<ExamPaper>()
            .Property(e => e.Sections)
            .HasConversion(
                v => JsonSerializer.Serialize(v, JsonSerializerOptions),
                v => JsonSerializer.Deserialize<List<ExamSection>>(v, JsonSerializerOptions));
    }
}