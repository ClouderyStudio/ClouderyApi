using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace ClouderyApi.Modules.Cloudery.Infrastructure.Persistence;

/// <summary>
/// 设计时工厂：让 dotnet ef 无需启动 Web 主机即可使用 ClouderyContext 生成迁移。
/// 运行时连接串由 Program.cs 注入，此处仅用于 migrations add / database update。
/// </summary>
public class ClouderyContextFactory : IDesignTimeDbContextFactory<ClouderyContext>
{
    public ClouderyContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
                               ?? "server=localhost;port=3306;database=api;user=root;password=root;";

        var options = new DbContextOptionsBuilder<ClouderyContext>()
            .UseMySQL(connectionString)
            .Options;

        return new ClouderyContext(options);
    }
}
