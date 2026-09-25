using ClouderyApi.Data;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 维护工具：清理对象存储中已不再被任何帖子 / 回复 / 头像引用的历史孤儿图片。
/// 由 `--sweep-orphans`（仅预览）与 `--sweep-orphans --delete-orphans`（实际删除）触发。
/// 比对按「子目录 + 文件名」进行，因此自定义域名、CDN 或 Bucket 前缀变更不会把正常图片误判为孤儿；
/// 也只处理 posts / avatars 两个子目录，不触碰同一 Bucket 内其它业务的对象。
/// </summary>
public static class MhopOrphanSweeper
{
    public static async Task<int> RunAsync(IServiceProvider services, bool delete, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(MhopOrphanSweeper));
        var db = services.GetRequiredService<MhopDbContext>();
        var storage = services.GetRequiredService<IMhopObjectStorage>();
        var uploads = services.GetRequiredService<MhopUploadService>();

        // ---- 1. 数据库里仍被引用的图片 ----
        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in await db.MhopPosts.AsNoTracking().Select(p => p.Images).ToListAsync(cancellationToken))
            foreach (var url in MhopImageRefs.Parse(raw)) Add(referenced, url);
        foreach (var raw in await db.MhopReplies.AsNoTracking().Select(r => r.Images).ToListAsync(cancellationToken))
            foreach (var url in MhopImageRefs.Parse(raw)) Add(referenced, url);
        foreach (var avatar in await db.MhopUsers.AsNoTracking().Select(u => u.Avatar).ToListAsync(cancellationToken))
            Add(referenced, avatar);

        // ---- 2. 对象存储里实际存在的对象 ----
        var objects = await storage.ListAsync(cancellationToken);
        var orphans = objects
            .Where(url => Tail(url) is { } tail && IsManaged(tail) && !referenced.Contains(tail))
            .ToList();

        logger.LogInformation("存储 {Provider}：对象 {Objects} 个，数据库引用 {Referenced} 个，孤儿 {Orphans} 个",
            storage.Provider, objects.Count, referenced.Count, orphans.Count);
        foreach (var url in orphans.Take(30)) logger.LogInformation("孤儿图片：{Url}", url);
        if (orphans.Count > 30) logger.LogInformation("其余 {Rest} 个未列出", orphans.Count - 30);

        if (!delete)
        {
            logger.LogInformation("预览结束，未删除任何对象；确认后追加 --delete-orphans 重新执行");
            return 0;
        }

        await uploads.DeleteAsync(orphans, cancellationToken);
        logger.LogInformation("已请求删除 {Count} 个孤儿对象", orphans.Count);
        return 0;
    }

    /// <summary>只处理本业务管理的两个子目录。</summary>
    private static bool IsManaged(string tail)
        => tail.StartsWith("posts/", StringComparison.Ordinal)
           || tail.StartsWith("avatars/", StringComparison.Ordinal);

    private static void Add(HashSet<string> set, string? url)
    {
        if (Tail(url) is { } tail) set.Add(tail);
    }

    /// <summary>取地址最后两段（子目录 / 文件名），忽略域名与桶前缀差异。</summary>
    private static string? Tail(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var parts = url.Trim().TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2 ? null : parts[^2] + "/" + parts[^1];
    }
}
