using ClouderyApi.Shared.Online;
using ClouderyApi.Shared.Redis;
using ClouderyApi.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace ClouderyApi.Tests;

/// <summary>进程内在线人数（未配置 Redis 时的默认实现，也是 Redis 版的降级兜底）的语义测试。</summary>
public sealed class InMemoryOnlineTrackerStoreTests
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(90);

    [Fact]
    public async Task Heartbeat_counts_distinct_keys_and_ignores_repeats()
    {
        var store = new InMemoryOnlineTrackerStore();

        Assert.Equal(1, await store.HeartbeatAsync("a", Window));
        Assert.Equal(2, await store.HeartbeatAsync("b", Window));
        Assert.Equal(2, await store.HeartbeatAsync("a", Window));
        Assert.Equal(2, await store.CountAsync(Window));
    }

    [Fact]
    public async Task Count_on_an_empty_store_is_zero()
    {
        var store = new InMemoryOnlineTrackerStore();

        Assert.Equal(0, await store.CountAsync(Window));
    }

    [Fact]
    public async Task Clients_outside_the_window_are_dropped()
    {
        var store = new InMemoryOnlineTrackerStore();
        var shortWindow = TimeSpan.FromMilliseconds(50);

        Assert.Equal(1, await store.HeartbeatAsync("a", shortWindow));
        await Task.Delay(120);

        Assert.Equal(0, await store.CountAsync(shortWindow));
        // 下一次心跳顺手清掉过期项，于是只剩新客户端。
        Assert.Equal(1, await store.HeartbeatAsync("b", shortWindow));
    }
}

/// <summary>Redis 不可用时降级（fail-open）：在线人数退回进程内计数，既不抛异常也不中断心跳。</summary>
public sealed class RedisOnlineTrackerStoreFallbackTests
{
    [Fact]
    public async Task Unreachable_redis_degrades_to_in_memory_counting()
    {
        var store = new RedisOnlineTrackerStore(
            RedisTestServer.CreateConnection(RedisTestServer.UnreachableConnectionString),
            NullLogger<RedisOnlineTrackerStore>.Instance);
        var window = TimeSpan.FromSeconds(90);

        Assert.Equal(1, await store.HeartbeatAsync("k1", window));
        Assert.Equal(2, await store.HeartbeatAsync("k2", window));
        Assert.Equal(2, await store.CountAsync(window));
    }
}

/// <summary>Redis 实机：心跳跨实例共享同一个 ZSET、键带 KeyPrefix，且键有 2 倍窗口的 TTL。</summary>
public sealed class RedisOnlineTrackerStoreTests
{
    [RedisFact]
    public async Task Heartbeats_are_shared_across_instances_and_keys_carry_the_prefix()
    {
        var connectionString = RedisTestServer.ConnectionString!;
        var window = TimeSpan.FromSeconds(90);
        var redisKey = RedisTestServer.KeyPrefix + "online";

        using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var database = connection.GetDatabase();

        try
        {
            await database.KeyDeleteAsync(redisKey);
            var first = new RedisOnlineTrackerStore(
                RedisTestServer.CreateConnection(connectionString), NullLogger<RedisOnlineTrackerStore>.Instance);
            var second = new RedisOnlineTrackerStore(
                RedisTestServer.CreateConnection(connectionString), NullLogger<RedisOnlineTrackerStore>.Instance);

            Assert.Equal(1, await first.HeartbeatAsync("test:" + Guid.NewGuid().ToString("N"), window));
            Assert.Equal(2, await second.HeartbeatAsync("test:" + Guid.NewGuid().ToString("N"), window));

            // 第二个实例看到的是同一份在线名单。
            Assert.Equal(2, await first.CountAsync(window));
            Assert.InRange(
                await database.KeyTimeToLiveAsync(redisKey) ?? TimeSpan.Zero,
                TimeSpan.FromSeconds(150),
                TimeSpan.FromSeconds(180));
        }
        finally
        {
            await database.KeyDeleteAsync(redisKey);
        }
    }
}
