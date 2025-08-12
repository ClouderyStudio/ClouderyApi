using ClouderyApi.Models;
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
            modelBuilder.Entity<ZhuxsTerm>()
                .Property(e => e.Information)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
                    v => JsonSerializer.Deserialize<TermInfo>(v, new JsonSerializerOptions()));

            modelBuilder.Entity<ZhuxsTerm>()
                .Property(e => e.Files)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
                    v => JsonSerializer.Deserialize<List<TermFile>>(v, new JsonSerializerOptions()));

            modelBuilder.Entity<ZhuxsApplication>()
                .Property(e => e.Sharables)
                .HasConversion(
                    v => JsonSerializer.Serialize(v, new JsonSerializerOptions()),
                    v => JsonSerializer.Deserialize<List<Sharable>>(v, new JsonSerializerOptions()));
        }
        public DbSet<ClouderyApi.Models.Whitelist> Whitelists { get; set; } = default!;
        public DbSet<ClouderyApi.Models.ZhuxsFile> ZhuxsFiles { get; set; } = default!;
        public DbSet<ClouderyApi.Models.ZhuxsTerm> ZhuxsTerms { get; set; } = default!;
        public DbSet<ClouderyApi.Models.ZhuxsApplication> ZhuxsApplications { get; set; } = default!;
    }
}
