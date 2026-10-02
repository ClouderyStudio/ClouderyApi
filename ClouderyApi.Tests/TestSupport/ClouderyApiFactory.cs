using System.Data;
using ClouderyApi.Modules.Cloudery.Infrastructure.Persistence;
using ClouderyApi.Modules.Zhuxs.Infrastructure.Persistence;
using ClouderyApi.Modules.Identity.Infrastructure.Persistence;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MySql.Data.MySqlClient;

namespace ClouderyApi.Tests.TestSupport;

/// <summary>
/// 用真实的 <c>Program</c> 启动 API，并把三个 DbContext 指向一次性测试库。
/// 连接串走环境变量注入：WebApplicationBuilder 的配置源顺序里环境变量排在 appsettings.json 之后，
/// 可以稳定压过仓库里那份指向生产库的配置；UseSetting 作为双保险。
/// </summary>
public sealed class ClouderyApiFactory : WebApplicationFactory<Program>
{
    public ClouderyApiFactory(string databaseName)
    {
        DatabaseName = databaseName;
        ConnectionString = MySqlTestServer.ConnectionStringFor(databaseName);

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString);
        Environment.SetEnvironmentVariable("Mhop__AutoMigrate", "true");
        Environment.SetEnvironmentVariable("Mhop__Seed", "true");
        // 测试不触碰对象存储，统一走本地磁盘实现。
        Environment.SetEnvironmentVariable("Mhop__Storage__Provider", "local");
    }

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Mhop:AutoMigrate", "true");
        builder.UseSetting("Mhop:Seed", "true");
        builder.UseSetting("Mhop:Storage:Provider", "local");

        // [AdminOnly] 每次请求都从 IConfiguration 读 Authorization:Admins，这里追加一个测试管理员；
        // ConfigureAppConfiguration 的源排在 appsettings.json 之后，能覆盖索引 0 的值。
        builder.ConfigureAppConfiguration(configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authorization:Admins:0"] = AuthCookie.TestAdminCasdoorId,
        }));
    }

    /// <summary>
    /// 启动期只自动迁移 MhopDbContext；另外两个上下文的表在这里补齐。
    /// ClouderyContext 的 ClouderyMembers 表早于 EF 迁移（迁移只覆盖 ExamPapers / ExamResults），
    /// 所以直接按 EF 模型生成建表脚本，让测试库 schema 与当前模型一致；
    /// ZhuxsContext 走自己的迁移（独立历史表 + 幂等 baseline），顺带验证 baseline 的真实建表能力。
    /// </summary>
    public async Task PrepareAuxiliarySchemasAsync()
    {
        using var scope = Services.CreateScope();
        var provider = scope.ServiceProvider;

        var cloudery = provider.GetRequiredService<ClouderyContext>();
        var connection = (MySqlConnection)cloudery.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open) await connection.OpenAsync();
        new MySqlScript(connection, cloudery.Database.GenerateCreateScript()).Execute();

        var zhuxs = provider.GetRequiredService<ZhuxsContext>();
        await zhuxs.Database.MigrateAsync();

        var identity = provider.GetRequiredService<IdentityDbContext>();
        await identity.Database.MigrateAsync();
    }
}
