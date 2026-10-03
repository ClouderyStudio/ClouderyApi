using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Modules.Mhop.Infrastructure;

/// <summary>
/// 数据库维护动作（迁移 / 种子数据）。
/// 部署脚本在重启应用前以 CLI 方式显式调用（dotnet ClouderyApi.dll --migrate --seed），
/// 启动期只保留由配置开关控制的兜底调用，避免每次启动都无条件改库。
/// 迁移一律前滚，禁止依赖 Down()。
/// </summary>
public sealed class DatabaseMaintenanceService(
    MhopDbContext db,
    MhopPasswordHasher passwordHasher,
    IOptions<MhopOptions> options,
    ILogger<DatabaseMaintenanceService> logger)
{
    /// <summary>应用所有待执行的 EF 迁移。</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("数据库迁移完成（MhopDbContext）");
    }

    /// <summary>写入幂等种子数据（管理员账号、引导数据）；MhopSeeder 内部按存在性判断，可重复执行。</summary>
    public Task SeedAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("开始写入 MHOP 种子数据");
        return MhopSeeder.SeedAsync(db, passwordHasher, options.Value, logger);
    }
}
