using System.Net;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;
using MySql.Data.MySqlClient;

namespace ClouderyApi.Tests;

/// <summary>
/// 写冲突 409 的真实路径（C.6②）：数据库层拒绝 INSERT 时，应用层把 DbUpdateException
/// 翻译成 ClouderyWriteConflictException / ZhuxsWriteConflictException，控制器渲染成 { detail: "记录冲突" }。
/// Zhuxs 三表没有唯一索引、主键也都是服务端生成的 GUID/字符串，正常请求撞不上库，
/// 所以这里用 BEFORE INSERT 触发器让 MySQL 真实报错（EF 会把它包成 DbUpdateException）。
/// </summary>
public sealed class WriteConflictContractTests : IntegrationTestBase
{
    private void SignInAsAdmin()
        => Client.DefaultRequestHeaders.Add(
            "Cookie",
            AuthCookie.CreateHeader(Factory.Services, AuthCookie.TestAdminCasdoorId));

    private async Task BlockInsertsAsync(string table)
    {
        await using var connection = new MySqlConnection(MySqlTestServer.ConnectionStringFor(Factory.DatabaseName));
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"CREATE TRIGGER block_insert_{table} BEFORE INSERT ON `{table}` FOR EACH ROW " +
            "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'contract-test blocked insert'";
        await command.ExecuteNonQueryAsync();
    }

    private static void AssertConflict(HttpResponseMessage response, JsonDocument body)
    {
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(new[] { "detail" }, body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("记录冲突", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Member_insert_rejected_by_the_database_returns_409()
    {
        SignInAsAdmin();
        await BlockInsertsAsync("ClouderyMembers");

        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/cloudery/members",
            new { name = "新成员", position = "职位" });
        var (_, body) = await JsonHttp.ReadAsync(response);

        AssertConflict(response, body);
    }

    [Fact]
    public async Task Whitelist_insert_rejected_by_the_database_returns_409()
    {
        SignInAsAdmin();
        await BlockInsertsAsync("ZhuxsWhitelists");

        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/zhuxs/whitelists",
            new { code = "INVITE-409" });
        var (_, body) = await JsonHttp.ReadAsync(response);

        AssertConflict(response, body);
    }
}
