using System.Data;
using ClouderyApi.Modules.Cloudery.Infrastructure.Persistence;
using ClouderyApi.Modules.Zhuxs.Infrastructure.Persistence;
using ClouderyApi.Modules.Identity.Infrastructure.Persistence;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Modules.Scforge.Infrastructure.Persistence;
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
    /// <summary>种子超管账号名与口令，仅供测试断言使用（生产种子不接受写死口令）。</summary>
    public const string TestSeedAdminUsername = "admin";
    public const string TestSeedAdminPassword = "test-only-admin-pwd";

    public ClouderyApiFactory(string databaseName)
    {
        DatabaseName = databaseName;
        ConnectionString = MySqlTestServer.ConnectionStringFor(databaseName);

        Environment.SetEnvironmentVariable("ConnectionStrings__DefaultConnection", ConnectionString);
        Environment.SetEnvironmentVariable("Mhop__AutoMigrate", "true");
        Environment.SetEnvironmentVariable("Mhop__Seed", "true");
        // 种子默认生成随机超管口令（生产不写死口令）；测试需要一个可预测的已知口令，
        // 才能断言登录与管理员契约。生产配置里不会出现这个键。
        Environment.SetEnvironmentVariable("Mhop__SeedAdminPassword", TestSeedAdminPassword);
        // 测试不触碰对象存储，统一走本地磁盘实现。
        Environment.SetEnvironmentVariable("Mhop__Storage__Provider", "local");
        // SCForge 的上传目录指向系统临时目录：测试不应往仓库工作树里写插件包与图片。
        Environment.SetEnvironmentVariable("Scforge__UploadDir", ScforgeUploadRoot);
        // 集成测试始终走本地存储，不碰真实 OSS。
        Environment.SetEnvironmentVariable("Scforge__Storage__Provider", "local");

        // Casdoor 节在 Program.cs 构建服务阶段（AddCasdoor）就被读取，那时只有 CreateBuilder 的
        // 环境变量源可见；ConfigureAppConfiguration 追加的源要等宿主最终配置才生效，来不及。
        // CI 检出里没有 appsettings.json（.gitignore 忽略），缺 ApplicationType 会让 Casdoor SDK
        // 抛 ArgumentOutOfRangeException，宿主起不来，全部集成测试一起挂。
        Environment.SetEnvironmentVariable("Casdoor__Endpoint", "https://casdoor.example.com");
        Environment.SetEnvironmentVariable("Casdoor__OrganizationName", "test");
        Environment.SetEnvironmentVariable("Casdoor__ApplicationName", "test");
        Environment.SetEnvironmentVariable("Casdoor__ApplicationType", "webapi");
        Environment.SetEnvironmentVariable("Casdoor__ClientId", "test-client-id");
        Environment.SetEnvironmentVariable("Casdoor__ClientSecret", "test-client-secret");
        Environment.SetEnvironmentVariable("Casdoor__CallbackPath", "/callback");
    }

    /// <summary>SCForge 上传目录（系统临时目录下的一次性位置）。</summary>
    public static string ScforgeUploadRoot { get; } =
        Path.Combine(Path.GetTempPath(), "cloudery-tests-scforge");

    public string DatabaseName { get; }

    public string ConnectionString { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("Mhop:AutoMigrate", "true");
        builder.UseSetting("Mhop:Seed", "true");
        builder.UseSetting("Mhop:SeedAdminPassword", TestSeedAdminPassword);
        builder.UseSetting("Mhop:Storage:Provider", "local");
        builder.UseSetting("Scforge:UploadDir", ScforgeUploadRoot);
        builder.UseSetting("Scforge:Storage:Provider", "local");
        builder.UseSetting("Casdoor:Endpoint", "https://casdoor.example.com");
        builder.UseSetting("Casdoor:OrganizationName", "test");
        builder.UseSetting("Casdoor:ApplicationName", "test");
        builder.UseSetting("Casdoor:ApplicationType", "webapi");
        builder.UseSetting("Casdoor:ClientId", "test-client-id");
        builder.UseSetting("Casdoor:ClientSecret", "test-client-secret");
        builder.UseSetting("Casdoor:CallbackPath", "/callback");

        // [AdminOnly] 每次请求都从 IConfiguration 读 Authorization:Admins，这里追加一个测试管理员；
        // ConfigureAppConfiguration 的源排在 appsettings.json 之后，能覆盖索引 0 的值。
        // 注意：这里的源只在宿主最终配置里生效（请求期读 IConfiguration 的代码看得到），
        // 构建服务阶段直读 builder.Configuration 的配置项（如 Casdoor）必须走上面的环境变量。
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

        var scforge = provider.GetRequiredService<ScforgeDbContext>();
        await scforge.Database.MigrateAsync();
    }
}
