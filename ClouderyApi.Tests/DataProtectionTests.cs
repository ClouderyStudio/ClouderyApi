using ClouderyApi.Shared.Redis;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace ClouderyApi.Tests;

/// <summary>
/// Data Protection 密钥环落点：默认内容根下的 <c>keys/</c>，可用 <c>DataProtection:KeysDirectory</c> 改写
/// （相对路径按内容根解析）。这一落点决定容器重建后 Cookie 登录态是否被清空，也决定多实例能否共用密钥环。
/// 用例只做文件系统落点，不需要 Redis；配了 Redis 时密钥环改存 Redis，此处只断言不再创建目录。
/// </summary>
public sealed class DataProtectionTests
{
    private const string Purpose = "ClouderyApi.Tests.DataProtection";

    [Fact]
    public void Keys_land_in_the_configured_directory_and_survive_a_new_provider()
    {
        using var temp = new TempDirectory();
        var keysDirectory = Path.Combine(temp.Path, "custom-keys");

        string payload;
        using (var provider = CreateProvider(temp.Path, settings =>
                   settings["DataProtection:KeysDirectory"] = keysDirectory))
        {
            var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
            payload = protector.Protect("hello");
            Assert.Equal("hello", protector.Unprotect(payload));
            Assert.NotEmpty(Directory.GetFiles(keysDirectory, "*.xml"));
        }

        // 第二次启动（等价于容器重建）从同一目录读回密钥：旧密文仍然解得开。
        using (var provider = CreateProvider(temp.Path, settings =>
                   settings["DataProtection:KeysDirectory"] = keysDirectory))
        {
            var protector = provider.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose);
            Assert.Equal("hello", protector.Unprotect(payload));
        }
    }

    [Fact]
    public void Empty_directory_falls_back_to_content_root_keys()
    {
        using var temp = new TempDirectory();

        using (var provider = CreateProvider(temp.Path))
        {
            Assert.NotNull(provider.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("x"));
        }

        Assert.NotEmpty(Directory.GetFiles(Path.Combine(temp.Path, "keys"), "*.xml"));
    }

    [Fact]
    public void Relative_directory_resolves_against_the_content_root()
    {
        using var temp = new TempDirectory();
        var expected = Path.Combine(temp.Path, "dp-relative");

        using (var provider = CreateProvider(temp.Path, settings =>
                   settings["DataProtection:KeysDirectory"] = "dp-relative"))
        {
            Assert.NotNull(provider.GetRequiredService<IDataProtectionProvider>().CreateProtector(Purpose).Protect("x"));
        }

        Assert.NotEmpty(Directory.GetFiles(expected, "*.xml"));
    }

    [Fact]
    public void Redis_backend_does_not_create_a_keys_directory()
    {
        using var temp = new TempDirectory();
        var keysDirectory = Path.Combine(temp.Path, "keys");

        using var provider = CreateProvider(temp.Path, settings =>
        {
            settings["DataProtection:KeysDirectory"] = keysDirectory;
            settings["Redis:ConnectionString"] = "127.0.0.1:1,abortConnect=false,connectTimeout=500,syncTimeout=500";
        });

        // 只解析 provider（还没用到密钥环）：注册成功即可，文件系统落点不应被创建。
        Assert.NotNull(provider.GetRequiredService<IDataProtectionProvider>());
        Assert.False(Directory.Exists(keysDirectory));
    }

    private static ServiceProvider CreateProvider(string contentRoot, Action<Dictionary<string, string?>>? configure = null)
    {
        var settings = new Dictionary<string, string?>();
        configure?.Invoke(settings);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddClouderyDataProtection(configuration, new HostingEnvironment
        {
            ContentRootPath = contentRoot,
            EnvironmentName = "Test",
            ApplicationName = "ClouderyApi",
        });

        return services.BuildServiceProvider();
    }

    /// <summary>一次性临时目录，测试结束即整棵删除。</summary>
    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "cloudery-tests-dp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // 临时目录清理失败不影响测试结论。
            }
        }
    }
}
