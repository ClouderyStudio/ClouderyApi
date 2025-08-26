using ClouderyApi.Models.Zhuxs;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClouderyApi.Data
{
    public class ClouderyApiContext : DbContext
    {
        public ClouderyApiContext (DbContextOptions<ClouderyApiContext> options)
            : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Term>()
                .Property(e => e.Information)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
                    v => JsonSerializer.Deserialize<TermInfo>(v, new JsonSerializerOptions()));

            modelBuilder.Entity<Term>()
                .Property(e => e.Files)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
                    v => JsonSerializer.Deserialize<List<TermFile>>(v, new JsonSerializerOptions()));

            modelBuilder.Entity<Application>()
                .Property(e => e.Sharables)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
                    v => JsonSerializer.Deserialize<List<Sharable>>(v, new JsonSerializerOptions()));
        }
        public DbSet<ClouderyApi.Models.Zhuxs.Whitelist> ZhuxsWhitelists { get; set; } = default!;
        public DbSet<ClouderyApi.Models.Zhuxs.Term> ZhuxsTerms { get; set; } = default!;
        public DbSet<ClouderyApi.Models.Zhuxs.Application> ZhuxsApplications { get; set; } = default!;
    }
}
