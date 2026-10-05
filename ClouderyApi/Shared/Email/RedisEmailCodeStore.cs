using System.Security.Cryptography;
using ClouderyApi.Shared.Redis;
using StackExchange.Redis;

namespace ClouderyApi.Shared.Email;

/// <summary>
/// Redis 验证码存储：多实例共享，因此扩容后同一个验证码在任何实例都能校验通过。
/// <para>
/// 三个键：<c>emailcode:code:{email}</c>（HASH：code / attempts，TTL 到验证码过期）、
/// <c>emailcode:sent:{email}</c>（发码时间戳，TTL 一个重发间隔，用来实现 60 秒频控）、
/// <c>emailcode:ip:{ip}</c>（ZSET 滑动窗口，member 带随机后缀保证同一秒内多次发码不互相覆盖）。
/// 发码与校验都在 Lua 里完成（先判断再写入），否则并发的两次请求会同时通过频控。
/// </para>
/// <para>
/// **fail-closed**：验证码是安全凭证，Redis 不可用时拒绝发码、拒绝校验，
/// 让用户稍后重试；绝不能因为存储故障而放行登录或跳过 IP 配额。
/// </para>
/// <para>
/// 校验用 Lua 的字符串比较，不是恒定时间；但验证码是 6 位数字且最多 5 次尝试，
/// 盲猜命中概率可忽略（1e-6 量级 × 5 次），因此这里不额外做恒定时间比较。
/// </para>
/// </summary>
public sealed class RedisEmailCodeStore : IEmailCodeStore
{
    /// <summary>
    /// IP 滑动窗口配额：KEYS[1]=ZSET，ARGV[1]=当前秒、ARGV[2]=窗口秒数、ARGV[3]=上限、ARGV[4]=本次 member。
    /// 返回 1 表示放行（已计入），0 表示超限。
    /// </summary>
    private const string IpRateScript =
        "redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[1] - ARGV[2]) " +
        "if redis.call('ZCARD', KEYS[1]) >= tonumber(ARGV[3]) then return 0 end " +
        "redis.call('ZADD', KEYS[1], ARGV[1], ARGV[4]) " +
        "redis.call('PEXPIRE', KEYS[1], ARGV[2] * 1000) " +
        "return 1";

    /// <summary>
    /// 发码：KEYS[1]=验证码 HASH、KEYS[2]=发码时间戳键；
    /// ARGV[1]=验证码、ARGV[2]=重发间隔秒数、ARGV[3]=验证码 TTL 秒数。
    /// 返回 { 剩余等待秒数 }：0 表示已发出，>0 表示频控中还剩多久。
    /// </summary>
    private const string IssueScript =
        "local ttl = redis.call('TTL', KEYS[2]) " +
        "if ttl > 0 then return { ttl + 1 } end " +
        "redis.call('SET', KEYS[2], '1', 'EX', ARGV[2]) " +
        "redis.call('HSET', KEYS[1], 'code', ARGV[1], 'attempts', 0) " +
        "redis.call('EXPIRE', KEYS[1], ARGV[3]) " +
        "return { 0 }";

    /// <summary>
    /// 校验并一次性消费：KEYS[1]=验证码 HASH，ARGV[1]=用户提交的验证码、ARGV[2]=最大尝试次数。
    /// 返回 1 表示通过（键已删除，一次性），0 表示错误 / 过期 / 尝试次数用尽。
    /// </summary>
    private const string VerifyScript =
        "local code = redis.call('HGET', KEYS[1], 'code') " +
        "if not code then return 0 end " +
        "local attempts = tonumber(redis.call('HGET', KEYS[1], 'attempts') or '0') " +
        "if attempts >= tonumber(ARGV[2]) then redis.call('DEL', KEYS[1]) return 0 end " +
        "if code == ARGV[1] then redis.call('DEL', KEYS[1]) return 1 end " +
        "redis.call('HINCRBY', KEYS[1], 'attempts', 1) " +
        "return 0";

    private readonly RedisConnection _connection;
    private readonly ILogger<RedisEmailCodeStore> _logger;
    private readonly RedisFallbackThrottle _throttle = new();

    public RedisEmailCodeStore(RedisConnection connection, ILogger<RedisEmailCodeStore> logger)
    {
        _connection = connection;
        _logger = logger;
    }

    public async ValueTask<bool> CheckIpRateAsync(string ip)
    {
        var database = _connection.GetDatabase();
        if (database is null)
        {
            LogUnavailable();
            return false;
        }

        try
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var reply = await database.ScriptEvaluateAsync(
                IpRateScript,
                new RedisKey[] { _connection.Key($"emailcode:ip:{ip}") },
                new RedisValue[]
                {
                    now,
                    EmailCodeDefaults.IpWindowSeconds,
                    EmailCodeDefaults.IpHourlyLimit,
                    $"{now}:{Guid.NewGuid():N}",
                }).ConfigureAwait(false);

            return (long)reply == 1;
        }
        catch (Exception ex)
        {
            LogFailure(ex, "IP 配额");
            return false;
        }
    }

    public async ValueTask<EmailCodeIssueResult> IssueCodeAsync(string email)
    {
        var database = _connection.GetDatabase();
        if (database is null)
        {
            LogUnavailable();
            return new EmailCodeIssueResult(null, EmailCodeDefaults.ResendIntervalSeconds);
        }

        try
        {
            // 与进程内实现一致：6 位数字，不校验邮箱是否存在（防枚举）。
            var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
            var reply = await database.ScriptEvaluateAsync(
                IssueScript,
                new RedisKey[]
                {
                    _connection.Key($"emailcode:code:{email}"),
                    _connection.Key($"emailcode:sent:{email}"),
                },
                new RedisValue[] { code, EmailCodeDefaults.ResendIntervalSeconds, EmailCodeDefaults.CodeTtlSeconds })
                .ConfigureAwait(false);

            var wait = (long)((RedisResult[]?)reply ?? [])[0];
            return wait > 0
                ? new EmailCodeIssueResult(null, (int)wait)
                : new EmailCodeIssueResult(code, 0);
        }
        catch (Exception ex)
        {
            LogFailure(ex, "发码");
            return new EmailCodeIssueResult(null, EmailCodeDefaults.ResendIntervalSeconds);
        }
    }

    public async ValueTask<bool> VerifyCodeAsync(string email, string code)
    {
        var database = _connection.GetDatabase();
        if (database is null)
        {
            LogUnavailable();
            return false;
        }

        try
        {
            var reply = await database.ScriptEvaluateAsync(
                VerifyScript,
                new RedisKey[] { _connection.Key($"emailcode:code:{email}") },
                new RedisValue[] { (code ?? string.Empty).Trim(), EmailCodeDefaults.MaxAttempts })
                .ConfigureAwait(false);

            return (long)reply == 1;
        }
        catch (Exception ex)
        {
            LogFailure(ex, "校验");
            return false;
        }
    }

    private void LogUnavailable()
    {
        if (_throttle.ShouldLog())
        {
            _logger.LogWarning("Redis 不可用，邮箱验证码按 fail-closed 处理（拒绝发码与登录）");
        }
    }

    private void LogFailure(Exception ex, string operation)
    {
        if (_throttle.ShouldLog())
        {
            _logger.LogWarning(ex, "Redis 验证码{Operation}失败，按 fail-closed 处理", operation);
        }
    }
}
