using ClouderyApi.Shared.Options;
using ClouderyApi.Shared.Redis;

namespace ClouderyApi.Shared.Email;

/// <summary>邮箱验证码存储的注册入口。</summary>
public static class EmailCodeStoreExtensions
{
    /// <summary>
    /// 注册 <see cref="IEmailCodeStore"/>：<c>Redis:ConnectionString</c> 非空时用 Redis
    /// （多实例共享验证码），留空时用进程内实现（本地开发、CI、集成测试零依赖）。
    /// </summary>
    public static IServiceCollection AddEmailCodeStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddRedisConnection(configuration);

        var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        if (redisOptions.IsConfigured)
        {
            services.AddSingleton<IEmailCodeStore, RedisEmailCodeStore>();
        }
        else
        {
            services.AddSingleton<IEmailCodeStore, InMemoryEmailCodeStore>();
        }

        return services;
    }
}
