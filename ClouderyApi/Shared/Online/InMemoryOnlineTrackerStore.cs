using System.Collections.Concurrent;

namespace ClouderyApi.Shared.Online;

/// <summary>
/// 进程内在线人数（未配置 Redis 时的默认实现，也是 Redis 版的降级兜底）。
/// 单容器部署足够；多实例时各算各的，重启即清零。
/// </summary>
public sealed class InMemoryOnlineTrackerStore : IOnlineTrackerStore
{
    private readonly ConcurrentDictionary<string, long> _lastSeen = new(StringComparer.Ordinal);

    public ValueTask<int> HeartbeatAsync(string key, TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMilliseconds = (long)window.TotalMilliseconds;

        _lastSeen[key] = now;
        foreach (var pair in _lastSeen)
        {
            if (now - pair.Value > windowMilliseconds) _lastSeen.TryRemove(pair.Key, out _);
        }

        return ValueTask.FromResult(_lastSeen.Count);
    }

    public ValueTask<int> CountAsync(TimeSpan window)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMilliseconds = (long)window.TotalMilliseconds;
        return ValueTask.FromResult(_lastSeen.Count(pair => now - pair.Value <= windowMilliseconds));
    }
}
