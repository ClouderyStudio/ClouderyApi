using System.Net;
using System.Text;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// 云端同步（exam/results）契约：未登录是 401 而非重定向、错误体是 {success,message}、
/// 成功体是 ExamResultSyncOut 的声明顺序、clientKey 幂等且 savedAt 只前进不后退。
/// </summary>
public sealed class ExamResultsContractTests : IntegrationTestBase
{
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private void SignIn()
        => Client.DefaultRequestHeaders.Add("Cookie", AuthCookie.CreateHeader(Factory.Services, userId: UserId));

    private async Task<(HttpStatusCode Status, JsonElement Body)> SyncAsync(string clientKey, string testTitle, string savedAt, int score)
    {
        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/exam/results/sync", new
        {
            records = new[]
            {
                new { clientKey, testId = "phq9", testTitle, savedAt, payload = new { score } },
            },
        });
        var (status, body) = await JsonHttp.ReadAsync(response);
        return (status, body.RootElement.Clone());
    }

    [Fact]
    public async Task List_requires_login_with_unified_error_shape()
    {
        var response = await Client.GetAsync("/exam/results");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var (_, body) = await JsonHttp.ReadAsync(response);
        Assert.False(body.RootElement.TryGetProperty("success", out _));
        Assert.Equal("未登录，无法使用云端同步", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Mutating_endpoints_require_login()
    {
        foreach (var method in new[] { HttpMethod.Post, HttpMethod.Delete })
        {
            var response = await JsonHttp.SendAsync(Client, method, "/exam/results", new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var (_, body) = await JsonHttp.ReadAsync(response);
            Assert.False(body.RootElement.TryGetProperty("success", out _));
            Assert.Equal("未登录，无法使用云端同步", body.RootElement.GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task Post_without_body_is_rejected_before_the_action_runs()
    {
        SignIn();
        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/exam/results");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task Post_with_json_null_returns_unified_validation_shape()
    {
        SignIn();
        var request = new HttpRequestMessage(HttpMethod.Post, "/exam/results")
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json"),
        };

        var response = await Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var (_, body) = await JsonHttp.ReadAsync(response);
        // 控制器里的 body == null 分支（"缺少请求体"）在 [ApiController] 下不可达。
        Assert.Equal("参数校验失败", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Sync_with_empty_records_uploads_zero_and_keeps_key_order()
    {
        SignIn();

        var (status, body) = await JsonHttp.PostJsonAsync(
            Client, "/exam/results/sync", new { records = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            new[] { "success", "uploaded", "total", "results" },
            body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, body.RootElement.GetProperty("uploaded").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("results").ValueKind);
    }

    [Fact]
    public async Task Sync_is_idempotent_per_client_key_and_never_regresses_on_older_saved_at()
    {
        SignIn();

        var first = await SyncAsync("k1", "PHQ-9", "2026-01-02T03:04:05Z", 7);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Assert.Equal(1, first.Body.GetProperty("uploaded").GetInt32());
        Assert.Equal(1, first.Body.GetProperty("total").GetInt32());

        var second = await SyncAsync("k1", "PHQ-9", "2026-01-02T03:04:05Z", 7);
        Assert.Equal(HttpStatusCode.OK, second.Status);
        Assert.Equal(1, second.Body.GetProperty("total").GetInt32());
        var secondRow = Assert.Single(second.Body.GetProperty("results").EnumerateArray().ToArray());
        Assert.Equal("k1", secondRow.GetProperty("clientKey").GetString());

        var stale = await SyncAsync("k1", "旧标题", "2026-01-01T00:00:00Z", 99);
        Assert.Equal(HttpStatusCode.OK, stale.Status);
        Assert.Equal(0, stale.Body.GetProperty("uploaded").GetInt32());
        Assert.Equal(1, stale.Body.GetProperty("total").GetInt32());
        var row = Assert.Single(stale.Body.GetProperty("results").EnumerateArray().ToArray());
        Assert.Equal(
            new[] { "id", "clientKey", "testId", "testTitle", "savedAt", "updatedAt", "payload" },
            row.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal("PHQ-9", row.GetProperty("testTitle").GetString());
        Assert.Equal(7, row.GetProperty("payload").GetProperty("score").GetInt32());
    }

    [Fact]
    public async Task Delete_unknown_returns_404_and_delete_by_client_key_returns_200()
    {
        SignIn();

        var unknown = await JsonHttp.SendAsync(Client, HttpMethod.Delete, "/exam/results/no-such-key");
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        var (_, unknownBody) = await JsonHttp.ReadAsync(unknown);
        Assert.Equal("记录不存在", unknownBody.RootElement.GetProperty("detail").GetString());

        await SyncAsync("k-del", "PHQ-9", "2026-01-02T03:04:05Z", 1);
        var deleted = await JsonHttp.SendAsync(Client, HttpMethod.Delete, "/exam/results/k-del");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);
        var (_, deletedBody) = await JsonHttp.ReadAsync(deleted);
        Assert.True(deletedBody.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("已删除", deletedBody.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Clear_reports_success_deleted_message_in_order()
    {
        SignIn();
        await SyncAsync("k-clear", "PHQ-9", "2026-01-02T03:04:05Z", 1);

        var response = await JsonHttp.SendAsync(Client, HttpMethod.Delete, "/exam/results");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var (_, body) = await JsonHttp.ReadAsync(response);
        Assert.Equal(
            new[] { "success", "deleted", "message" },
            body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(1, body.RootElement.GetProperty("deleted").GetInt32());
        Assert.Equal("云端记录已清空", body.RootElement.GetProperty("message").GetString());
    }
}
