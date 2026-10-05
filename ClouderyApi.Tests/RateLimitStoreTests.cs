using ClouderyApi.Shared.RateLimit;
using ClouderyApi.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ClouderyApi.Tests;

/// <summary>
/// 进程内限流计数（未配置 Redis 时的默认实现，也是降级兜底）的语义测试：不依赖任何外部服务。
/// </summary>
public sealed class InMemoryRateLimitStoreTests
{
    [Fact]
    public async Task Count_increments_within_the_window_and_reports_remaining_seconds()
    {
        var store = new InMemoryRateLimitStore();

        var first = await store.IncrementAsync("k", 60);
        var second = await store.IncrementAsync("k", 60);

        Assert.Equal(1L, first.Count);
        Assert.Equal(2L, second.Count);
        Assert.InRange(first.RetryAfterSeconds, 59, 60);
        Assert.InRange(second.RetryAfterSeconds, 58, 60);
    }

    [Fact]
    public async Task Counters_are_isolated_by_key()
    {
        var store = new InMemoryRateLimitStore();

        await store.IncrementAsync("a", 60);
        var other = await store.IncrementAsync("b", 60);

        Assert.Equal(1L, other.Count);
    }

    [Fact]
    public async Task Window_restarts_after_it_expires()
    {
        var store = new InMemoryRateLimitStore();

        await store.IncrementAsync("k", 1);
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var afterExpiry = await store.IncrementAsync("k", 1);

        Assert.Equal(1L, afterExpiry.Count);
    }
}

/// <summary>
/// Redis 不可用时的降级（fail-open）：计数退回进程内实现，既不抛异常也不放行到无限。
/// 用必然连不上的回环端口模拟，因此 CI 不需要 Redis。
/// </summary>
public sealed class RedisRateLimitStoreFallbackTests
{
    [Fact]
    public async Task Unreachable_redis_degrades_to_in_memory_counting()
    {
        var store = CreateStore("127.0.0.1:1,abortConnect=false,connectTimeout=500,syncTimeout=500");

        var first = await store.IncrementAsync("k", 60);
        var second = await store.IncrementAsync("k", 60);

        Assert.Equal(1L, first.Count);
        Assert.Equal(2L, second.Count);
    }

    private static RedisRateLimitStore CreateStore(string connectionString) => new(
        RedisTestServer.CreateConnection(connectionString),
        NullLogger<RedisRateLimitStore>.Instance);
}

/// <summary>
/// Redis 实机语义：不同 store 实例看到同一个计数（跨实例共享）、键带 KeyPrefix、窗口有 TTL。
/// </summary>
public sealed class RedisRateLimitStoreTests
{
    [RedisFact]
    public async Task Counters_are_shared_across_instances_and_keys_carry_the_prefix()
    {
        var connectionString = RedisTestServer.ConnectionString!;
        var key = "ratelimit:" + Guid.NewGuid().ToString("N");

        using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var database = connection.GetDatabase();

        try
        {
            // 两个独立的连接/实例，模拟两个进程共享同一份计数。
            var first = new RedisRateLimitStore(
                RedisTestServer.CreateConnection(connectionString), NullLogger<RedisRateLimitStore>.Instance);
            var second = new RedisRateLimitStore(
                RedisTestServer.CreateConnection(connectionString), NullLogger<RedisRateLimitStore>.Instance);

            Assert.Equal(1L, (await first.IncrementAsync(key, 60)).Count);
            var secondCounter = await second.IncrementAsync(key, 60);

            // 第二个实例看到的是同一个计数（不是各算各的）。
            Assert.Equal(2L, secondCounter.Count);
            Assert.InRange(secondCounter.RetryAfterSeconds, 59, 60);

            var redisKey = RedisTestServer.KeyPrefix + key;
            Assert.True(await database.KeyExistsAsync(redisKey));
            Assert.InRange(
                await database.KeyTimeToLiveAsync(redisKey) ?? TimeSpan.Zero,
                TimeSpan.FromSeconds(55),
                TimeSpan.FromSeconds(60));
        }
        finally
        {
            await database.KeyDeleteAsync(RedisTestServer.KeyPrefix + key);
        }
    }
}
