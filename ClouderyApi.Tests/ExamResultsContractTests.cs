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

    [Fact]
    public async Task Sync_without_records_field_is_treated_as_empty_instead_of_500()
    {
        SignIn();

        // 只取回、不上传：records 缺失曾经会在 SyncAsync 里 records.Count 抛 NRE（500）。
        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/exam/results/sync", new { });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, body.RootElement.GetProperty("uploaded").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("results").ValueKind);
    }

    /// <summary>
    /// 附录 C.4：写入与读回的时间必须逐字相同（截到微秒 + 统一 BeijingDateTimeConverter 以 +08:00 呈现），
    /// 否则客户端按 ISO-8601 解析会得到错误的时刻。
    /// </summary>
    [Fact]
    public async Task Saved_at_and_updated_at_round_trip_identically_and_use_beijing_offset()
    {
        SignIn();

        var first = await SyncAsync("k-utc", "PHQ-9", "2026-01-02T03:04:05.123456Z", 7);
        Assert.Equal(HttpStatusCode.OK, first.Status);
        var row = first.Body.GetProperty("results")[0];
        var savedAt = row.GetProperty("savedAt").GetString()!;
        var updatedAt = row.GetProperty("updatedAt").GetString()!;
        Assert.EndsWith("+08:00", savedAt);
        Assert.EndsWith("+08:00", updatedAt);

        var (listStatus, list) = await JsonHttp.GetJsonAsync(Client, "/exam/results");
        Assert.Equal(HttpStatusCode.OK, listStatus);
        var readBack = list.RootElement.GetProperty("results")[0];

        // 写路径（内存里的现在值）与读路径（MySQL datetime(6) 往返）逐字相同。
        Assert.Equal(savedAt, readBack.GetProperty("savedAt").GetString());
        Assert.Equal(updatedAt, readBack.GetProperty("updatedAt").GetString());

        var parsed = DateTimeOffset.Parse(savedAt, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None);
        // 呈现是北京时间，时刻仍是写入的那个 UTC 瞬间。
        Assert.Equal(TimeSpan.FromHours(8), parsed.Offset);
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, 123, DateTimeKind.Utc).AddTicks(4560), parsed.UtcDateTime);
        Assert.Equal(0, parsed.UtcDateTime.Ticks % TimeSpan.TicksPerMicrosecond);
    }

    [Fact]
    public async Task Saved_at_without_timezone_is_persisted_as_utc()
    {
        SignIn();

        var (status, body) = await SyncAsync("k-noz", "PHQ-9", "2026-01-02T03:04:05", 1);

        Assert.Equal(HttpStatusCode.OK, status);
        var savedAt = body.GetProperty("results")[0].GetProperty("savedAt").GetString()!;
        Assert.EndsWith("+08:00", savedAt);
        Assert.Equal(
            new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc),
            DateTimeOffset.Parse(savedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None).UtcDateTime);
    }

    [Fact]
    public async Task Sync_rejects_more_than_200_records()
    {
        SignIn();

        var records = Enumerable.Range(0, 201)
            .Select(i => new { clientKey = $"k{i}", testId = "phq9", payload = new { score = i } })
            .ToArray();

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/exam/results/sync", new { records });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("一次最多同步 200 条记录，请分批上传", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Sync_rejects_oversized_payload()
    {
        SignIn();

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/exam/results/sync", new
        {
            records = new[] { new { clientKey = "k-big", testId = "phq9", payload = new string('x', 300_000) } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("单条结果过大，无法上传到云端", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Sync_rejects_record_without_payload()
    {
        SignIn();

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/exam/results/sync", new
        {
            records = new[] { new { clientKey = "k-nopayload", testId = "phq9" } },
        });

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("记录缺少结果正文（payload）", body.RootElement.GetProperty("detail").GetString());
    }

    /// <summary>每用户最多保留最新的 200 条：第三次同步后最早的一批被裁掉。</summary>
    [Fact]
    public async Task Sync_prunes_to_the_newest_200_records_per_user()
    {
        SignIn();

        for (var batch = 0; batch < 3; batch++)
        {
            var records = Enumerable.Range(0, 100).Select(i => new
            {
                clientKey = $"p-{batch}-{i}",
                testId = "phq9",
                savedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(batch * 100 + i)
                    .ToString("O"),
                payload = new { score = i },
            }).ToArray();

            var (status, body) = await JsonHttp.PostJsonAsync(Client, "/exam/results/sync", new { records });
            Assert.Equal(HttpStatusCode.OK, status);
            if (batch == 2) Assert.Equal(200, body.RootElement.GetProperty("total").GetInt32());
        }

        var (listStatus, list) = await JsonHttp.GetJsonAsync(Client, "/exam/results");
        Assert.Equal(HttpStatusCode.OK, listStatus);
        Assert.Equal(200, list.RootElement.GetProperty("total").GetInt32());
        var rows = list.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(200, rows.Length);
        Assert.DoesNotContain(rows, r => r.GetProperty("clientKey").GetString()!.StartsWith("p-0-"));
        Assert.Contains(rows, r => r.GetProperty("clientKey").GetString() == "p-2-99");
    }

}

