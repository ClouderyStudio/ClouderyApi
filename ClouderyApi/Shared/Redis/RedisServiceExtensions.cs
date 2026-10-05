using ClouderyApi.Shared.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ClouderyApi.Shared.Redis;

/// <summary>共享 Redis 连接的注册入口。</summary>
public static class RedisServiceExtensions
{
    /// <summary>
    /// 注册 <c>Redis</c> 配置节与共享的 <see cref="RedisConnection"/>。可重复调用：
    /// 限流 / 在线人数 / 验证码 / 密钥环各自声明依赖，但整个进程只会建立一条连接。
    /// 未配置连接串时也注册，只是没人会去用它。
    /// </summary>
    public static IServiceCollection AddRedisConnection(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.TryAddSingleton<RedisConnection>();
        return services;
    }
}
