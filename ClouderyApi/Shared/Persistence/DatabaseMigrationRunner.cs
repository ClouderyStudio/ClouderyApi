using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Shared.Persistence;

/// <summary>
/// 数据库迁移的统一前滚入口：发现本程序集里全部 <see cref="DbContext"/>，
/// 逐个应用待执行迁移。生产由部署脚本在重启容器前以 CLI 方式调用
/// （<c>dotnet ClouderyApi.dll --migrate</c>），失败向上抛并以非 0 退出码中止部署。
/// <para>
/// 为什么按程序集自动发现而不是维护手工清单：2026-10-04 事故是
/// 「<c>--migrate</c> 只驱动 <c>MhopDbContext</c>，<c>ScforgeDbContext</c> 从未纳入」——
/// 新增迁移后流水线全绿、部署全绿，但新表从未建出来，带 Bearer 的请求一律 500。
/// 手工清单每加一个上下文就要记得同步一次，这里改为列出「所有 DbContext」，
/// 让漏接在机制上不可能发生。
/// </para>
/// 迁移一律前滚，禁止依赖 <c>Down()</c>。
/// </summary>
public static class DatabaseMigrationRunner
{
    /// <summary>
    /// 本程序集里全部非抽象 <see cref="DbContext"/> 派生类型，按类型名排序（顺序稳定、日志可复现）。
    /// 新增 DbContext 会被自动纳入 <c>--migrate</c>；未注册进 DI 的上下文会在解析时显式报错。
    /// </summary>
    public static IReadOnlyList<Type> DiscoverContextTypes() =>
        typeof(DatabaseMigrationRunner).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(DbContext).IsAssignableFrom(type))
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>从 DI 解析每个已发现的上下文并逐个前滚。</summary>
    public static async Task MigrateAllAsync(
        IServiceProvider serviceProvider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        foreach (var contextType in DiscoverContextTypes())
        {
            var db = (DbContext)serviceProvider.GetRequiredService(contextType);
            await MigrateAsync(db, logger, cancellationToken);
        }
    }

    /// <summary>前滚单个上下文：无待执行迁移只记一条 Info，有待执行的先记清单再执行。</summary>
    public static async Task MigrateAsync(
        DbContext db,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var contextName = db.GetType().Name;
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("数据库无需迁移（{Context}：无待执行迁移）", contextName);
            return;
        }

        logger.LogInformation(
            "开始迁移 {Context}，待执行 {Count} 个：{Pending}",
            contextName,
            pending.Count,
            string.Join(", ", pending));
        await db.Database.MigrateAsync(cancellationToken);
        logger.LogInformation("数据库迁移完成（{Context}）", contextName);
    }
}
