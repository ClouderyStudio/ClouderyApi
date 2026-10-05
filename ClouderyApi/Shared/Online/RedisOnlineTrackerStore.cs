using ClouderyApi.Shared.Redis;
using StackExchange.Redis;

namespace ClouderyApi.Shared.Online;

/// <summary>
/// Redis ZSET 在线人数：member = 客户端 key，score = 最后一次心跳的毫秒时间戳。
/// 心跳在 Lua 里原子完成「打点 + 清理过期 member + 续期 + 计数」，
/// 计数直接按 score 范围统计，因此多实例共享同一份在线名单。
/// <para>
/// 在线人数只是展示用的近似值，**fail-open**：Redis 不可用时退回进程内计数，
/// 不能让「看有多少人在线」这种接口因为 Redis 抖动变成 500。
/// </para>
/// </summary>
public sealed class RedisOnlineTrackerStore : IOnlineTrackerStore
{
    private const string Key = "online";

    /// <summary>
    /// KEYS[1] 为 ZSET；ARGV[1]=当前毫秒时间戳、ARGV[2]=member、ARGV[3]=过期阈值(now-window)、
    /// ARGV[4]=键的 TTL 毫秒（取 2 倍窗口，窗口内没人心跳时键自然消失，避免留下空 ZSET）。
    /// </summary>
    private const string HeartbeatScript =
        "redis.call('ZADD', KEYS[1], ARGV[1], ARGV[2]) " +
        "redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[3]) " +
        "redis.call('PEXPIRE', KEYS[1], ARGV[4]) " +
        "return redis.call('ZCARD', KEYS[1])";

    private readonly RedisConnection _connection;
    private readonly ILogger<RedisOnlineTrackerStore> _logger;
    private readonly InMemoryOnlineTrackerStore _fallback = new();
    private readonly RedisFallbackThrottle _throttle = new();

    public RedisOnlineTrackerStore(RedisConnection connection, ILogger<RedisOnlineTrackerStore> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async ValueTask<int> HeartbeatAsync(string key, TimeSpan window)
    {
        var database = _connection.GetDatabase();
        if (database is null) return await _fallback.HeartbeatAsync(key, window).ConfigureAwait(false);

        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var windowMilliseconds = (long)window.TotalMilliseconds;
            var reply = await database.ScriptEvaluateAsync(
                HeartbeatScript,
                new RedisKey[] { _connection.Key(Key) },
                new RedisValue[]
                {
                    now,
                    key,
                    now - windowMilliseconds,
                    windowMilliseconds * 2,
                }).ConfigureAwait(false);

            return (int)(long)reply;
        }
        catch (Exception ex)
        {
            if (_throttle.ShouldLog())
            {
                _logger.LogWarning(ex, "Redis 在线人数统计失败，暂时降级为进程内计数（key={Key}）", key);
            }

            return await _fallback.HeartbeatAsync(key, window).ConfigureAwait(false);
        }
    }

    public async ValueTask<int> CountAsync(TimeSpan window)
    {
        var database = _connection.GetDatabase();
        if (database is null) return await _fallback.CountAsync(window).ConfigureAwait(false);

        try
        {
            var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)window.TotalMilliseconds;
            var count = await database
                .SortedSetLengthAsync(_connection.Key(Key), cutoff, double.PositiveInfinity)
                .ConfigureAwait(false);

            return (int)count;
        }
        catch (Exception ex)
        {
            if (_throttle.ShouldLog())
            {
                _logger.LogWarning(ex, "Redis 在线人数统计失败，暂时降级为进程内计数");
            }

            return await _fallback.CountAsync(window).ConfigureAwait(false);
        }
    }
}
