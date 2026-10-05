namespace ClouderyApi.Shared.Redis;

/// <summary>
/// Redis 降级日志的节流闸：Redis 长时间不可用时，每个请求都写一条 warning 会把日志淹掉，
/// 这里保证最多每 30 秒放行一条（并发下也只有一个调用方拿到 true）。
/// </summary>
internal sealed class RedisFallbackThrottle
{
    private const long IntervalMilliseconds = 30_000;

    private long _lastTicks;

    /// <summary>是否应该写出这条降级日志。</summary>
    public bool ShouldLog()
    {
        var now = Environment.TickCount64;
        var last = Interlocked.Read(ref _lastTicks);
        if (now - last < IntervalMilliseconds) return false;
        return Interlocked.CompareExchange(ref _lastTicks, now, last) == last;
    }
}
