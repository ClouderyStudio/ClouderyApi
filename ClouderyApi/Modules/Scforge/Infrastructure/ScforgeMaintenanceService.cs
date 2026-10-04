using ClouderyApi.Modules.Scforge.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// SCForge 域的数据库迁移入口。
///
/// 为什么单独一个服务：<c>--migrate</c> 原本只驱动 <c>DatabaseMaintenanceService</c>（MHOP 域），
/// 而该服务只持有 <c>MhopDbContext</c>。SCForge 的 <c>scforge_*</c> 表当初是靠手工
/// <c>dotnet ef database update --context ScforgeDbContext</c> 建上的，因此此后每新增一个迁移，
/// 部署流水线都是**绿的、但新表根本没建** —— 表现是带 Key 的请求一律 500（表不存在），
/// 而公开端点因为共用这个中间件被一并打挂。
///
/// 这里让部署脚本的同一条 <c>--migrate</c> 命令把 SCForge 的迁移也一并前滚，
/// 从此"加迁移"不再依赖人记得手动补一刀。
/// </summary>
public sealed class ScforgeMaintenanceService(
    ScforgeDbContext db,
    ILogger<ScforgeMaintenanceService> logger)
{
    /// <summary>应用 SCForge 域所有待执行的 EF 迁移。</summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("数据库迁移完成（ScforgeDbContext）");
    }
}
