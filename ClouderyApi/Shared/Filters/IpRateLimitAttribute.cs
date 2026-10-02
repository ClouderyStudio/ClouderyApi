using System.Collections.Concurrent;
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

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (MaxRequests <= 0 || WindowSeconds <= 0) return;

        var path = context.HttpContext.Request.Path.Value ?? string.Empty;
        var ip = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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
        // 用 ObjectResult（走 MVC 输出格式化器）而不是 JsonResult，中文不会被转成 \uXXXX，
        // 与 Cloudery 模块其它接口的响应风格保持一致。
        context.Result = new ObjectResult(new
        {
            success = false,
            message = "分析请求过于频繁，请稍后再试",
            retryAfterSeconds = retryAfter,
        })
        {
            StatusCode = StatusCodes.Status429TooManyRequests,
        };
    }

    /// <summary>顺带清理过期窗口，避免长期运行后字典无限增长。</summary>
    private void Sweep(long now)
    {
        if (now - Interlocked.Read(ref _lastSweepUnixSeconds) < 600) return;
        Interlocked.Exchange(ref _lastSweepUnixSeconds, now);

        foreach (var (key, window) in Hits)
        {
            if (now - window.WindowStart >= WindowSeconds) Hits.TryRemove(key, out _);
        }
    }

    private sealed record Window(int Count, long WindowStart);
}
