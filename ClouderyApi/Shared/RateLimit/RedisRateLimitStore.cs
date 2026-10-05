using ClouderyApi.Shared.Redis;
using StackExchange.Redis;

namespace ClouderyApi.Shared.RateLimit;

/// <summary>
/// Redis 固定窗口计数：<c>INCR</c> 后首次 <c>EXPIRE</c>，整段放在一段 Lua 里原子执行
/// （若分两步发命令，中间断开就会留下永不过期的键，等于该 key 被永久封禁）。
/// 窗口从 key 首次出现起算，与 <see cref="InMemoryRateLimitStore"/> 语义一致。
/// <para>
/// 限流是「尽力而为」的防护：Redis 连不上或命令超时时降级为进程内计数（fail-open），
/// 绝不能让一次正常的点赞或发帖因为 Redis 不可用而变成 500。
/// 代价是 Redis 长期不可用时退化为单实例计数——日志里会留下 warning。
/// </para>
/// </summary>
public sealed class RedisRateLimitStore : IRateLimitStore
{
    /// <summary>
    /// KEYS[1] 为计数键，ARGV[1] 为窗口秒数；返回 { 计数, 剩余 TTL }。
    /// 只在计数为 1（窗口第一次请求）时设置过期，后续请求沿用原窗口，与进程内实现一致。
    /// </summary>
    private const string IncrementScript =
        "local count = redis.call('INCR', KEYS[1]) " +
        "if count == 1 then redis.call('EXPIRE', KEYS[1], ARGV[1]) end " +
        "return { count, redis.call('TTL', KEYS[1]) }";

    private readonly RedisConnection _connection;
    private readonly ILogger<RedisRateLimitStore> _logger;

    /// <summary>自带一份进程内实现作为兜底：Redis 不可用时限流退回接入前的行为，而不是放行或报错。</summary>
    private readonly InMemoryRateLimitStore _fallback = new();
    private readonly RedisFallbackThrottle _throttle = new();

    public RedisRateLimitStore(RedisConnection connection, ILogger<RedisRateLimitStore> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async ValueTask<RateLimitCounter> IncrementAsync(string key, int windowSeconds)
    {
        var database = _connection.GetDatabase();
        if (database is null) return await _fallback.IncrementAsync(key, windowSeconds).ConfigureAwait(false);

        try
        {
            var reply = await database
                .ScriptEvaluateAsync(
                    IncrementScript,
                    new RedisKey[] { _connection.Key(key) },
                    new RedisValue[] { windowSeconds })
                .ConfigureAwait(false);

            // 脚本固定返回 { 计数, 剩余 TTL } 两个整数；结构不符就当作 Redis 侧异常，走降级。
            var values = (RedisResult[]?)reply;
            if (values is null || values.Length < 2)
            {
                throw new InvalidOperationException($"Redis 限流脚本返回了非预期结果：{reply}");
            }

            var count = (long)values[0];
            // TTL 为 -1（键未设置过期）/ -2（键不存在）等异常值一律按「整窗口」处理。
            var remaining = (long)values[1];
            if (remaining <= 0) remaining = windowSeconds;
            return new RateLimitCounter(count, Math.Max(1, (int)Math.Min(remaining, windowSeconds)));
        }
        catch (Exception ex)
        {
            if (_throttle.ShouldLog())
            {
                _logger.LogWarning(ex, "Redis 限流计数失败，暂时降级为进程内计数（key={Key}）", key);
            }

            return await _fallback.IncrementAsync(key, windowSeconds).ConfigureAwait(false);
        }
    }
}
