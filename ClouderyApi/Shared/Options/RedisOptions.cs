namespace ClouderyApi.Shared.Options;

/// <summary>
/// Redis 连接配置（appsettings.json 的 <c>Redis</c> 节，也可用环境变量 <c>Redis__ConnectionString</c> 覆盖）。
/// <para>
/// <see cref="ConnectionString"/> 留空 = 未接入 Redis：限流计数留在进程内，
/// 本地开发、CI 与集成测试因此不需要任何 Redis 依赖（与接入前的行为完全一致）。
/// 填上连接串即切到共享计数：重启不再清零，多实例共用一个桶。
/// </para>
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// StackExchange.Redis 连接串，如 <c>127.0.0.1:6379,abortConnect=false</c>；
    /// 云 Redis 通常还需要 <c>password=***,ssl=true</c>，并建议收紧 <c>connectTimeout</c>。
    /// 留空则限流走进程内实现。
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>键前缀，避免与同一 Redis 实例上的其它应用撞键。</summary>
    public string KeyPrefix { get; set; } = "cloudery:";

    /// <summary>是否已配置 Redis（连接串非空白）。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ConnectionString);
}
