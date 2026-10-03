using System.Collections.Concurrent;
using ClouderyApi.Shared.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ClouderyApi.Shared.Filters;

/// <summary>
/// 按客户端 IP 的固定窗口限流，用于成本敏感或易被刷的公开接口（如 AI 解读）。
/// 全局中间件已有 300 次/分钟的宽松额度（Program.cs），这里用更紧的额度叠加限制单接口。
/// 计数放在静态字典里：本应用是单实例部署，与全局限流器保持同样的实现口径；
/// 若将来多实例，请改为共享存储（Redis）或网关限流。
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IpRateLimitAttribute : ActionFilterAttribute
{
    private static readonly ConcurrentDictionary<string, Window> Hits = new(StringComparer.Ordinal);
    private static long _lastSweepUnixSeconds;

    /// <summary>窗口内允许的请求数。</summary>
    public int MaxRequests { get; set; } = 10;

    /// <summary>窗口长度（秒）。</summary>
    public int WindowSeconds { get; set; } = 300;

    /// <summary>
    /// 429 响应的错误体形状。Cloudery / Zhuxs 返回裸对象 <c>{ success, message }</c>，
    /// MHOP 走MhopJson 的 <c>{ detail }</c>（与 Python FastAPI 一致）——挂在 MHOP 控制器上时必须显式打开，
    /// 否则前端读不到 <c>detail</c>。
    /// </summary>
    public bool UseMhopErrorShape { get; set; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (MaxRequests <= 0 || WindowSeconds <= 0) return;

        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        var ip = ClientIp.Resolve(context.HttpContext);
        var key = ip + "|" + path;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Sweep(now);

        var window = Hits.AddOrUpdate(
            key,
            _ => new Window(1, now),
            (_, current) => now - current.WindowStart >= WindowSeconds
                ? new Window(1, now)
                : new Window(current.Count + 1, current.WindowStart));

        if (window.Count <= MaxRequests) return;

        var retryAfter = Math.Max(1, WindowSeconds - (now - window.WindowStart));
        context.HttpContext.Response.Headers.RetryAfter = retryAfter.ToString();

        if (UseMhopErrorShape)
        {
            context.Result = MhopJson.Error(StatusCodes.Status429TooManyRequests, RateLimitMessage);
            return;
        }

        // 用 ObjectResult（走 MVC 输出格式化器）而不是 JsonResult，中文不会被转成 \uXXXX，
        // 与 Cloudery 模块其它接口的响应风格保持一致。
        context.Result = new ObjectResult(new
        {
            success = false,
            message = RateLimitMessage,
            retryAfterSeconds = retryAfter,
        })
        {
            StatusCode = StatusCodes.Status429TooManyRequests,
        };
    }

    /// <summary>限流文案。对外逐字不变，既有断言依赖它。</summary>
    private const string RateLimitMessage = "分析请求过于频繁，请稍后再试";

    /// <summary>
    /// 顺带清理过期窗口，避免长期运行后字典无限增长。
    /// 只清当前 path 前缀的条目：Hits 被所有限流端点共享，而各自的窗口长度不同，
    /// 用本实例的 WindowSeconds 判定会误删其他端点尚未过期的窗口（等于提前放行）。
    /// </summary>
    private void Sweep(long now)
    {
        if (now - Interlocked.Read(ref _lastSweepUnixSeconds) < 600) return;
        Interlocked.Exchange(ref _lastSweepUnixSeconds, now);

        foreach (var (key, window) in Hits)
        {
            // 只清理超过 1 小时的窗口：Hits 被所有限流端点共享且各自窗口长度不同，
            // 若用本实例的 WindowSeconds 判定，会误删其他端点尚未过期的活跃窗口（等于提前放行）。
            if (now - window.WindowStart >= MaxSweepAgeSeconds) Hits.TryRemove(key, out _);
        }
    }

    /// <summary>清理阈值：取 1 小时，足够覆盖当前所有窗口配置且不会误删活跃窗口。</summary>
    private const int MaxSweepAgeSeconds = 3600;

    private sealed record Window(int Count, long WindowStart);
}