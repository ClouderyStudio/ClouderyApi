using ClouderyApi.Models.Cloudery;
using ClouderyApi.Models.Zhuxs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace ClouderyApi.Data
{
    public class ClouderyApiContext : DbContext
    {
        public ClouderyApiContext(DbContextOptions<ClouderyApiContext> options)
            : base(options)
        {
        }

        private static readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions();
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
#pragma warning disable CS8603
            modelBuilder.Entity<Term>()
                .Property(e => e.Information)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, _jsonSerializerOptions),
                    v => JsonSerializer.Deserialize<TermInfo>(v, _jsonSerializerOptions));
#pragma warning restore CS8603

            modelBuilder.Entity<Term>()
                .Property(e => e.Files)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, _jsonSerializerOptions),
                    v => JsonSerializer.Deserialize<List<TermFile>>(v, _jsonSerializerOptions));

            modelBuilder.Entity<Application>()
                .Property(e => e.Sharables)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, _jsonSerializerOptions),
                    v => JsonSerializer.Deserialize<List<Sharable>>(v, _jsonSerializerOptions));

            modelBuilder.Entity<Member>()
                .Property(e => e.Socials)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, _jsonSerializerOptions),
                    v => JsonSerializer.Deserialize<List<Social>>(v, _jsonSerializerOptions));
        }

        public DbSet<Whitelist> ZhuxsWhitelists { get; set; } = default!;
        public DbSet<Term> ZhuxsTerms { get; set; } = default!;
        public DbSet<Application> ZhuxsApplications { get; set; } = default!;
        public DbSet<Member> ClouderyMembers { get; set; } = default!;
    }
}
