using ClouderyApi.Modules.Scforge.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>
/// SCForge 域的持久化边界：应用服务只依赖本接口，实现为独立的 ScforgeDbContext，
/// 便于领域逻辑脱离 MySQL 做单元测试。
/// </summary>
public interface IScforgeDbContext
{
    DbSet<ScforgePlugin> ScforgePlugins { get; }
    DbSet<ScforgeVersion> ScforgeVersions { get; }
    DbSet<ScforgeComment> ScforgeComments { get; }
    DbSet<ScforgeVote> ScforgeVotes { get; }
    DbSet<ScforgeAdmin> ScforgeAdmins { get; }
    DbSet<ScforgeGameVersion> ScforgeGameVersions { get; }
    DbSet<ScforgeApiKey> ScforgeApiKeys { get; }
    DbSet<ScforgeAccessGrant> ScforgeAccessGrants { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
