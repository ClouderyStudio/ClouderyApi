using MySql.Data.MySqlClient;

namespace ClouderyApi.Tests.TestSupport;

/// <summary>
/// 一次性 MySQL 测试库的创建与销毁。
/// 连接信息取自环境变量 <c>CLOUDERY_TEST_MYSQL</c>，形如
/// <c>server=127.0.0.1;port=3307;user=cloudery;password=cloudery</c>（不含 database 段）；
/// 未设置时回落到 E:.Cloudery\TestEnv 下的本地便携实例，CI 由 workflow 注入。
/// </summary>
public static class MySqlTestServer
{
    public const string EnvironmentVariable = "CLOUDERY_TEST_MYSQL";

    private const string LocalFallback = "server=127.0.0.1;port=3307;user=cloudery;password=cloudery";

    public static string BaseConnectionString { get; } =
        Environment.GetEnvironmentVariable(EnvironmentVariable) is { Length: > 0 } value
            ? value
            : LocalFallback;

    public static string NewDatabaseName() => "cloudery_test_" + Guid.NewGuid().ToString("N")[..16];

    public static string ConnectionStringFor(string databaseName) =>
        new MySqlConnectionStringBuilder(BaseConnectionString) { Database = databaseName }.ConnectionString;

    public static Task CreateDatabaseAsync(string databaseName) =>
        ExecuteAsync($"CREATE DATABASE `{databaseName}` CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;");

    public static Task DropDatabaseAsync(string databaseName) =>
        ExecuteAsync($"DROP DATABASE IF EXISTS `{databaseName}`;");

    private static async Task ExecuteAsync(string sql)
    {
        var admin = new MySqlConnectionStringBuilder(BaseConnectionString) { Pooling = false };
        await using var connection = new MySqlConnection(admin.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
