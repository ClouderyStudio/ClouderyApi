using System.Collections.Concurrent;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 在线人数：心跳 + 滑动窗口计数。单机内存实现，接口语义与 Redis 一致；
/// 上生产替换为 Redis ZSET 即可。
/// </summary>
public sealed class MhopOnlineTracker
{
    private const long WindowMilliseconds = 90_000;

    private readonly ConcurrentDictionary<string, long> _lastSeen = new(StringComparer.Ordinal);

    public int Heartbeat(string key)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _lastSeen[key] = now;
        foreach (var pair in _lastSeen)
        {
            if (now - pair.Value > WindowMilliseconds) _lastSeen.TryRemove(pair.Key, out _);
        }
        return _lastSeen.Count;
    }

    public int Count()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return _lastSeen.Count(pair => now - pair.Value <= WindowMilliseconds);
    }
}
