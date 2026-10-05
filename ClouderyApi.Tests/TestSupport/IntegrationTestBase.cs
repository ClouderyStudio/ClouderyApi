using ClouderyApi.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using MySql.Data.MySqlClient;

namespace ClouderyApi.Tests;

/// <summary>每个测试类一个一次性 MySQL 库 + 一个真实启动的 API 宿主。</summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    private string _databaseName = string.Empty;

    protected ClouderyApiFactory Factory { get; private set; } = null!;

    protected HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _databaseName = MySqlTestServer.NewDatabaseName();
        await MySqlTestServer.CreateDatabaseAsync(_databaseName);

        Factory = new ClouderyApiFactory(_databaseName);
        ConfigureFactory(Factory);
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        await Factory.PrepareAuxiliarySchemasAsync();
    }

    /// <summary>派生类可覆盖：在 CreateClient 之前配置工厂（例如替换上游 HttpClient 的处理器）。</summary>
    protected virtual void ConfigureFactory(ClouderyApiFactory factory)
    {
    }

    /// <summary>派生类可覆盖：在释放宿主之后停掉自己拉起的桩服务（先调用 base）。</summary>
    public virtual Task DisposeAsync()
    {
        Client?.Dispose();
        Factory?.Dispose();
        // 每个测试类自建一个一次性库和一个真实宿主，连接池按连接串（含库名）区分；
        // 不主动清理的话，几十个类留下的空闲连接会一直挂在 MySQL 上，
        // CI 上默认 max_connections=151 会被撞穿（Too many connections）。
        MySqlConnection.ClearAllPools();
        return string.IsNullOrEmpty(_databaseName)
            ? Task.CompletedTask
            : MySqlTestServer.DropDatabaseAsync(_databaseName);
    }
}
