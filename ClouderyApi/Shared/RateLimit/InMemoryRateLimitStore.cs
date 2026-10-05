using System.Collections.Concurrent;

namespace ClouderyApi.Shared.RateLimit;

/// <summary>
/// 进程内固定窗口计数：未配置 Redis 时的实现，也是 Redis 抖动 / 断连时的降级兜底。
/// 语义与 <see cref="RedisRateLimitStore"/> 一致，差别只在「重启清零、多实例各算各的」。
/// </summary>
public sealed class InMemoryRateLimitStore : IRateLimitStore
{
    private readonly ConcurrentDictionary<string, Window> _hits = new(StringComparer.Ordinal);
    private long _lastSweepUnixSeconds;

    /// <summary>清理阈值：超过 1 小时的窗口一律可清（当前最长窗口 300 秒，留足余量）。</summary>
    private const int MaxSweepAgeSeconds = 3600;

    /// <summary>两次清理之间的最小间隔，避免每个请求都遍历字典。</summary>
    private const int SweepIntervalSeconds = 600;

    public ValueTask<RateLimitCounter> IncrementAsync(string key, int windowSeconds)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Sweep(now);

        // AddOrUpdate 是单次原子操作；早先的「GetOrAdd → 读 → 改 → 写」四步存在竞态，
        // 并发下会丢失计数，实际放行量被放大数倍。
        var window = _hits.AddOrUpdate(
            key,
            _ => new Window(1, now),
            (_, current) => now - current.WindowStart >= windowSeconds
                ? new Window(1, now)
                : new Window(current.Count + 1, current.WindowStart));

        var retryAfter = Math.Max(1, windowSeconds - (int)(now - window.WindowStart));
        return ValueTask.FromResult(new RateLimitCounter(window.Count, retryAfter));
    }

    /// <summary>
    /// 顺带清理过期窗口，避免长期运行后字典无限增长。
    /// 只清超过 <see cref="MaxSweepAgeSeconds"/> 的窗口：该字典被所有限流端点共用且各自窗口长度不同，
    /// 若按调用方的 WindowSeconds 判定，会误删其它端点尚未过期的活跃窗口（等于提前放行）。
    /// </summary>
    private void Sweep(long now)
    {
        if (now - Interlocked.Read(ref _lastSweepUnixSeconds) < SweepIntervalSeconds) return;
        Interlocked.Exchange(ref _lastSweepUnixSeconds, now);

        foreach (var (key, window) in _hits)
        {
            if (now - window.WindowStart >= MaxSweepAgeSeconds) _hits.TryRemove(key, out _);
        }
    }

    private sealed record Window(long Count, long WindowStart);
}
