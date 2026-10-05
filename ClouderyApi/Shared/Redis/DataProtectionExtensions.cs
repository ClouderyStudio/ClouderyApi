using ClouderyApi.Shared.Options;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.StackExchangeRedis;

namespace ClouderyApi.Shared.Redis;

/// <summary>
/// Data Protection 密钥环的落点。框架默认把密钥写在容器的 <c>~/.aspnet/DataProtection-Keys</c>，
/// 该目录不在部署时挂载的卷里：容器一重建密钥就换新，所有 Cookie 会话（Casdoor 登录态）立即失效，
/// 表现为「更新一次服务，全体用户掉线」。
/// <para>
/// 配了 Redis 就落到 Redis（多实例共享同一密钥环，扩容后仍然只登录一次）；未配 Redis 则落到
/// <c>DataProtection:KeysDirectory</c>（默认内容根下的 <c>keys/</c>）。1Panel 部署把宿主目录挂到
/// <c>/app</c>，因此这个默认位置同样能在容器重建后保留密钥。
/// </para>
/// <para>
/// <see cref="IDataProtectionBuilder.SetApplicationName"/> 显式固定：默认应用名是内容根路径，
/// 开发机与容器不一致会让同一份密钥在两个环境互不通用。
/// </para>
/// </summary>
public static class DataProtectionExtensions
{
    /// <summary>密钥环在 Redis 中的键名（前缀由 <c>Redis:KeyPrefix</c> 补上）。</summary>
    public const string RedisKeyName = "dataprotection:keys";

    /// <summary>应用名。同一应用的所有实例必须一致，否则各用各的密钥、互相解不开。</summary>
    public const string ApplicationName = "ClouderyApi";

    /// <summary>注册密钥环存储：Redis 优先，否则落到内容根的可持久化目录。</summary>
    public static IServiceCollection AddClouderyDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddRedisConnection(configuration);

        var redisOptions = configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>() ?? new RedisOptions();
        var builder = services.AddDataProtection().SetApplicationName(ApplicationName);

        if (redisOptions.IsConfigured)
        {
            // 用 KeyManagementOptions 而不是 PersistKeysToStackExchangeRedis(Func<IDatabase>, ...)：
            // 密钥环要到宿主启动后才会真正读写，所以可以在回调里按需取 DI 里的共享连接
            // （RedisConnection 自己是懒连接，Redis 不可用也不会拖垮启动）。
            // 在 PersistKeysToFileSystem 之后注册，因此这里的落点最后生效。
            var redisKey = $"{redisOptions.KeyPrefix}{RedisKeyName}";
            services.AddOptions<KeyManagementOptions>()
                .Configure<IServiceProvider>((options, provider) =>
                {
                    options.XmlRepository = new RedisXmlRepository(
                        () => provider.GetRequiredService<RedisConnection>().GetDatabase()
                            ?? throw new InvalidOperationException(
                                "Redis:ConnectionString 无法解析，Data Protection 密钥环无法写入 Redis"),
                        redisKey);
                });
            return services;
        }

        var directory = configuration["DataProtection:KeysDirectory"];
        if (string.IsNullOrWhiteSpace(directory))
        {
            directory = Path.Combine(environment.ContentRootPath, "keys");
        }
        else if (!Path.IsPathRooted(directory))
        {
            directory = Path.Combine(environment.ContentRootPath, directory);
        }

        System.IO.Directory.CreateDirectory(directory);
        builder.PersistKeysToFileSystem(new DirectoryInfo(directory));
        return services;
    }
}
