using ClouderyApi.Shared.Options;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClouderyApi.Shared.Redis;

/// <summary>
/// 进程内共享的 Redis 连接。StackExchange.Redis 的多路复用器本身就是线程安全、按「一个进程一条连接、
/// 内部自动重连」设计的，因此限流计数、在线人数、邮箱验证码与 Data Protection 密钥环共用一个实例，
/// 而不是各自建连（每建一次都会多一条 socket 与一组后台定时器）。
/// <para>
/// 连接延迟到首次使用才建立，且 <c>AbortOnConnectFail=false</c>：Redis 不可用时进程照常启动，
/// 各功能按自己的语义降级——限流与在线人数 fail-open（退回进程内计数），验证码 fail-closed（拒绝）。
/// 连接串本身解析失败（配置写错）时 <see cref="Multiplexer"/> 为 null，调用方同样走各自的降级路径。
/// </para>
/// </summary>
public sealed class RedisConnection
{
    private readonly Lazy<IConnectionMultiplexer?> _multiplexer;

    public RedisConnection(IOptions<RedisOptions> options, ILogger<RedisConnection> logger)
    {
        var redisOptions = options.Value;
        KeyPrefix = string.IsNullOrWhiteSpace(redisOptions.KeyPrefix) ? string.Empty : redisOptions.KeyPrefix;
        _multiplexer = new Lazy<IConnectionMultiplexer?>(() => Connect(redisOptions.ConnectionString, logger));
    }

    /// <summary>键前缀（已保证非 null，可能为空串）。</summary>
    public string KeyPrefix { get; }

    /// <summary>多路复用器；连接串无法解析时为 null（调用方走降级实现）。</summary>
    public IConnectionMultiplexer? Multiplexer => _multiplexer.Value;

    /// <summary>数据库；Redis 不可用时为 null。</summary>
    public IDatabase? GetDatabase() => Multiplexer?.GetDatabase();

    /// <summary>给键补上配置的前缀，避免同一 Redis 实例上多应用撞键。</summary>
    public string Key(string key) => KeyPrefix + key;

    private static IConnectionMultiplexer? Connect(string connectionString, ILogger logger)
    {
        try
        {
            var options = ConfigurationOptions.Parse(connectionString);
            // 连不上不要抛异常、也不要因此让进程起不来：StackExchange.Redis 会在后台持续重连。
            options.AbortOnConnectFail = false;
            options.ClientName = "ClouderyApi";
            return ConnectionMultiplexer.Connect(options);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis 连接失败，相关功能将按各自策略降级（请检查 Redis:ConnectionString）");
            return null;
        }
    }
}
