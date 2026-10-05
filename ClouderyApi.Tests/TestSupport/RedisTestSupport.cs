using ClouderyApi.Shared.Options;
using ClouderyApi.Shared.Redis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Tests.TestSupport;

/// <summary>
/// 只在设置了 <c>CLOUDERY_TEST_REDIS</c>（如 <c>127.0.0.1:6379</c>）时运行；
/// CI 不配 Redis，这些用例会显示为跳过而不是失败。本地用便携版验证时显式设置该变量。
/// </summary>
public sealed class RedisFactAttribute : FactAttribute
{
    public RedisFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(RedisTestServer.ConnectionString))
        {
            Skip = "需要本地 Redis：设置 CLOUDERY_TEST_REDIS 后运行（CI 不配 Redis）";
        }
    }
}

/// <summary>Redis 实机测试的连接信息、测试键前缀，以及构造被测对象用的辅助。</summary>
internal static class RedisTestServer
{
    /// <summary>必然连不上的回环端口：用于 fail-open / fail-closed 降级用例，因此 CI 不需要 Redis。</summary>
    public const string UnreachableConnectionString =
        "127.0.0.1:1,abortConnect=false,connectTimeout=500,syncTimeout=500";

    public static string? ConnectionString { get; } = Environment.GetEnvironmentVariable("CLOUDERY_TEST_REDIS");

    /// <summary>测试键前缀：与生产默认的 <c>cloudery:</c> 分开，避免污染。</summary>
    public static string KeyPrefix => "cloudery:test:";

    /// <summary>构造一个指向实机 Redis 的共享连接（每个 store 一个，模拟不同实例）。</summary>
    public static RedisConnection CreateConnection(string connectionString) => new(
        Options.Create(new RedisOptions
        {
            ConnectionString = connectionString,
            KeyPrefix = KeyPrefix,
        }),
        NullLogger<RedisConnection>.Instance);

    /// <summary>构造指向 <c>CLOUDERY_TEST_REDIS</c> 的连接（调用方须先确认该变量已设置）。</summary>
    public static RedisConnection CreateRealConnection() => CreateConnection(ConnectionString!);
}
