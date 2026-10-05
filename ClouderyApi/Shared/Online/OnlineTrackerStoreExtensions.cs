using ClouderyApi.Shared.Options;
using ClouderyApi.Shared.Redis;

namespace ClouderyApi.Shared.Online;

/// <summary>在线人数存储的注册入口。</summary>
public static class OnlineTrackerStoreExtensions
{
    /// <summary>
    /// 注册 <see cref="IOnlineTrackerStore"/>：<c>Redis:ConnectionString</c> 非空时用 Redis
    /// （多实例共享在线名单），留空时用进程内实现（本地开发、CI、集成测试零依赖）。
    /// </summary>
    public static IServiceCollection AddOnlineTrackerStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRedisConnection(configuration);

        var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        if (redisOptions.IsConfigured)
        {
            services.AddSingleton<IOnlineTrackerStore, RedisOnlineTrackerStore>();
        }
        else
        {
            services.AddSingleton<IOnlineTrackerStore, InMemoryOnlineTrackerStore>();
        }

        return services;
    }
}
