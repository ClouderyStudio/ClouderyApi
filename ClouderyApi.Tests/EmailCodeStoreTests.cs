using ClouderyApi.Shared.Email;
using ClouderyApi.Shared.Redis;
using ClouderyApi.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using StackExchange.Redis;

namespace ClouderyApi.Tests;

/// <summary>进程内验证码存储（未配置 Redis 时的默认实现）的语义测试：不依赖外部服务。</summary>
public sealed class InMemoryEmailCodeStoreTests
{
    private static InMemoryEmailCodeStore CreateStore() => new(NullLogger<InMemoryEmailCodeStore>.Instance);

    [Fact]
    public async Task Issued_code_is_six_digits_and_verifies_exactly_once()
    {
        var store = CreateStore();
        const string email = "user@example.com";

        var issued = await store.IssueCodeAsync(email);

        Assert.NotNull(issued.Code);
        Assert.Matches("^[0-9]{6}$", issued.Code!);
        Assert.Equal(0, issued.RetryAfterSeconds);
        Assert.True(await store.VerifyCodeAsync(email, issued.Code!));
        // 一次性：同一个码不能再用第二次。
        Assert.False(await store.VerifyCodeAsync(email, issued.Code!));
    }

    [Fact]
    public async Task Resending_within_the_interval_is_rejected_with_remaining_seconds()
    {
        var store = CreateStore();
        const string email = "user@example.com";

        await store.IssueCodeAsync(email);
        var again = await store.IssueCodeAsync(email);

        Assert.Null(again.Code);
        // 同一秒内重发时剩余等待是整段间隔 +1（原实现同此语义）。
        Assert.InRange(again.RetryAfterSeconds, 1, EmailCodeDefaults.ResendIntervalSeconds + 1);
    }

    [Fact]
    public async Task Five_wrong_attempts_invalidate_the_code()
    {
        var store = CreateStore();
        const string email = "user@example.com";
        var issued = await store.IssueCodeAsync(email);
        Assert.NotNull(issued.Code);
        var wrong = issued.Code == "000000" ? "000001" : "000000";

        for (var i = 0; i < EmailCodeDefaults.MaxAttempts; i++)
        {
            Assert.False(await store.VerifyCodeAsync(email, wrong));
        }

        // 尝试次数用尽后条目被删除，正确的码也不再通过。
        Assert.False(await store.VerifyCodeAsync(email, issued.Code!));
    }

    [Fact]
    public async Task Ip_quota_stops_after_the_hourly_limit()
    {
        var store = CreateStore();
        const string ip = "203.0.113.7";

        for (var i = 0; i < EmailCodeDefaults.IpHourlyLimit; i++)
        {
            Assert.True(await store.CheckIpRateAsync(ip));
        }

        Assert.False(await store.CheckIpRateAsync(ip));
        // 其他 IP 不受影响。
        Assert.True(await store.CheckIpRateAsync("203.0.113.8"));
    }

    [Fact]
    public async Task Verifying_without_issuing_fails()
    {
        var store = CreateStore();

        Assert.False(await store.VerifyCodeAsync("nobody@example.com", "123456"));
    }
}

/// <summary>
/// Redis 不可用时验证码是 <b>fail-closed</b>：发码与校验直接拒绝，
/// 而不是退化到无频控 / 无校验——登录功能宁可暂时不可用。
/// </summary>
public sealed class RedisEmailCodeStoreFallbackTests
{
    [Fact]
    public async Task Unreachable_redis_rejects_sending_and_verifying()
    {
        var store = new RedisEmailCodeStore(
            RedisTestServer.CreateConnection(RedisTestServer.UnreachableConnectionString),
            NullLogger<RedisEmailCodeStore>.Instance);

        Assert.False(await store.CheckIpRateAsync("203.0.113.7"));

        var issued = await store.IssueCodeAsync("user@example.com");
        Assert.Null(issued.Code);
        Assert.Equal(EmailCodeDefaults.ResendIntervalSeconds, issued.RetryAfterSeconds);

        Assert.False(await store.VerifyCodeAsync("user@example.com", "123456"));
    }
}

/// <summary>Redis 实机：验证码跨实例共享、一次性消费、重发频控共享、键带 KeyPrefix 与 TTL。</summary>
public sealed class RedisEmailCodeStoreTests
{
    [RedisFact]
    public async Task Code_is_shared_across_instances_and_consumed_once()
    {
        var connectionString = RedisTestServer.ConnectionString!;
        var email = $"redis-test-{Guid.NewGuid():N}@example.com";
        var ip = "test-" + Guid.NewGuid().ToString("N");
        var codeKey = RedisTestServer.KeyPrefix + "emailcode:code:" + email;
        var sentKey = RedisTestServer.KeyPrefix + "emailcode:sent:" + email;
        var ipKey = RedisTestServer.KeyPrefix + "emailcode:ip:" + ip;

        using var connection = await ConnectionMultiplexer.ConnectAsync(connectionString);
        var database = connection.GetDatabase();

        try
        {
            var first = new RedisEmailCodeStore(
                RedisTestServer.CreateConnection(connectionString), NullLogger<RedisEmailCodeStore>.Instance);
            var second = new RedisEmailCodeStore(
                RedisTestServer.CreateConnection(connectionString), NullLogger<RedisEmailCodeStore>.Instance);

            Assert.True(await first.CheckIpRateAsync(ip));

            var issued = await first.IssueCodeAsync(email);
            Assert.NotNull(issued.Code);
            Assert.Matches("^[0-9]{6}$", issued.Code!);
            Assert.True(await database.KeyExistsAsync(codeKey));
            Assert.InRange(
                await database.KeyTimeToLiveAsync(codeKey) ?? TimeSpan.Zero,
                TimeSpan.FromSeconds(540),
                TimeSpan.FromSeconds(600));

            // 另一个实例能校验第一个实例发的码（跨实例共享），且只能成功一次。
            Assert.True(await second.VerifyCodeAsync(email, issued.Code!));
            Assert.False(await database.KeyExistsAsync(codeKey));
            Assert.False(await first.VerifyCodeAsync(email, issued.Code!));

            // 重发频控也在实例之间共享。
            var again = await second.IssueCodeAsync(email);
            Assert.Null(again.Code);
            Assert.InRange(again.RetryAfterSeconds, 1, EmailCodeDefaults.ResendIntervalSeconds + 1);
        }
        finally
        {
            await database.KeyDeleteAsync(codeKey);
            await database.KeyDeleteAsync(sentKey);
            await database.KeyDeleteAsync(ipKey);
        }
    }
}
