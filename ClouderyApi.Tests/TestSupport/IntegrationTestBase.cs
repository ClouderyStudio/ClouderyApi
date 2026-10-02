using ClouderyApi.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;

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
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

        await Factory.PrepareAuxiliarySchemasAsync();
    }

    public Task DisposeAsync()
    {
        Client?.Dispose();
        Factory?.Dispose();
        return string.IsNullOrEmpty(_databaseName)
            ? Task.CompletedTask
            : MySqlTestServer.DropDatabaseAsync(_databaseName);
    }
}
