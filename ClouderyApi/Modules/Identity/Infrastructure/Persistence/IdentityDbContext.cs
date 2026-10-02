using ClouderyApi.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// 身份域：本地登录用户。实体表名保持 Users，与历史库中的 Users 表同名，
/// 因此迁移到独立上下文后既有用户数据与 /exam/results 归属不变。
/// </summary>
public class IdentityDbContext(DbContextOptions<IdentityDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;
}
