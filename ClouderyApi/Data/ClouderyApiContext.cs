using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ClouderyApi.Models;

namespace ClouderyApi.Data
{
    public class ClouderyApiContext : DbContext
    {
        public ClouderyApiContext (DbContextOptions<ClouderyApiContext> options)
            : base(options)
        {
        }

        public DbSet<ClouderyApi.Models.Whitelist> Whitelists { get; set; } = default!;
    }
}
