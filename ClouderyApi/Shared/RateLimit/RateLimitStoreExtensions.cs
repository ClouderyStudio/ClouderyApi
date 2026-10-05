using ClouderyApi.Shared.Options;
using ClouderyApi.Shared.Redis;

namespace ClouderyApi.Shared.RateLimit;

/// <summary>限流计数存储的注册入口。</summary>
public static class RateLimitStoreExtensions
{
    /// <summary>
    /// 注册 <see cref="IRateLimitStore"/>：<c>Redis:ConnectionString</c> 非空时用 Redis（跨实例共享），
    /// 留空时用进程内实现（本地开发、CI、集成测试零依赖）。两种实现对外是同一套固定窗口语义。
    /// </summary>
    public static IServiceCollection AddRateLimitStore(this IServiceCollection services, IConfiguration configuration)
    {
        // 与在线人数 / 验证码 / Data Protection 共用同一条 Redis 连接。
        services.AddRedisConnection(configuration);
        var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();

        if (redisOptions.IsConfigured)
        {
            services.AddSingleton<IRateLimitStore, RedisRateLimitStore>();
        }
        else
        {
            services.AddSingleton<IRateLimitStore, InMemoryRateLimitStore>();
        }

        return services;
    }
}
