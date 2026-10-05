using ClouderyApi.Shared.Persistence;
using ClouderyApi.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MySql.Data.MySqlClient;

namespace ClouderyApi.Tests;

/// <summary>
/// <c>--migrate</c> 覆盖面的回归测试：DatabaseMigrationRunner 必须发现并前滚**全部** DbContext。
/// 2026-10-04 事故（部署全绿但新表不存在）的根因就是漏了一个上下文，
/// 所以这里用一个全新空库跑一遍真迁移，断言每个上下文都「无待执行迁移」且「已应用迁移非空」。
/// </summary>
public sealed class DatabaseMigrationRunnerTests : IAsyncLifetime
{
    private readonly string _databaseName = MySqlTestServer.NewDatabaseName();
    private ClouderyApiFactory _factory = null!;

    public async Task InitializeAsync()
    {
        await MySqlTestServer.CreateDatabaseAsync(_databaseName);
        _factory = new ClouderyApiFactory(_databaseName);
        // 刻意不调用 PrepareAuxiliarySchemasAsync：本测试要让 --migrate 自己把空库建起来。
    }

    public async Task DisposeAsync()
    {
        _factory.Dispose();
        MySqlConnection.ClearAllPools();
        await MySqlTestServer.DropDatabaseAsync(_databaseName);
    }

    [Fact]
    public void Discovered_contexts_cover_every_module()
    {
        var contextNames = DatabaseMigrationRunner.DiscoverContextTypes()
            .Select(type => type.Name)
            .ToArray();

        Assert.Equal(
            ["ClouderyContext", "IdentityDbContext", "MhopDbContext", "ScforgeDbContext", "ZhuxsContext"],
            contextNames);
    }

    [Fact]
    public async Task Migrate_all_rolls_forward_every_context_on_an_empty_database()
    {
        using var scope = _factory.Services.CreateScope();

        await DatabaseMigrationRunner.MigrateAllAsync(scope.ServiceProvider, NullLogger.Instance);

        foreach (var contextType in DatabaseMigrationRunner.DiscoverContextTypes())
        {
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        }
    }

    [Fact]
    public async Task Migration_is_idempotent_on_the_second_pass()
    {
        using var scope = _factory.Services.CreateScope();

        await DatabaseMigrationRunner.MigrateAllAsync(scope.ServiceProvider, NullLogger.Instance);
        await DatabaseMigrationRunner.MigrateAllAsync(scope.ServiceProvider, NullLogger.Instance);

        foreach (var contextType in DatabaseMigrationRunner.DiscoverContextTypes())
        {
            var db = (DbContext)scope.ServiceProvider.GetRequiredService(contextType);
            Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        }
    }
}
