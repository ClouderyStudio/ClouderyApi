using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClouderyApi.Tests;

/// <summary>
/// SCForge 对外 OpenAPI 文档（/swagger/scforge-public/swagger.json）的契约测试：
///   1. 只收录 /scforge 下的非 admin 端点，且端点集合与预期清单逐条一致；
///   2. 声明 scf_ API Key 安全方案，并挂到文档级 security；
///   3. 与入库产物 docs/openapi/scforge-public.json 无漂移（缺失时写出产物并失败，重跑即绿）。
/// 产物是 ClouderyDoc 文档站 /api 分区的数据源，接口有改动必须重跑本测试重新生成并提交。
/// </summary>
public sealed class ScforgePublicOpenApiTests : IntegrationTestBase
{
    private const string DocumentUrl = "/swagger/scforge-public/swagger.json";

    /// <summary>预期对外端点（"METHOD /path"，Ordinal 排序）。</summary>
    private static readonly string[] ExpectedRoutes =
    [
        "DELETE /scforge/comments/{id}",
        "DELETE /scforge/comments/{id}/vote",
        "DELETE /scforge/plugins/{id}",
        "DELETE /scforge/plugins/{id}/vote",
        "DELETE /scforge/versions/{id}",
        "GET /scforge/api-keys",
        "GET /scforge/api-keys/scopes",
        "GET /scforge/game-versions",
        "GET /scforge/plugins",
        "GET /scforge/plugins/featured",
        "GET /scforge/plugins/mine",
        "GET /scforge/plugins/mine/summary",
        "GET /scforge/plugins/recent",
        "GET /scforge/plugins/{idOrSlug}",
        "GET /scforge/plugins/{id}/vote",
        "GET /scforge/plugins/{pluginId}/comments",
        "GET /scforge/versions/{id}/download",
        "PATCH /scforge/comments/{id}",
        "PATCH /scforge/versions/{id}",
        "POST /scforge/api-keys",
        "POST /scforge/api-keys/{id}/revoke",
        "POST /scforge/api-keys/{id}/rotate",
        "POST /scforge/plugins",
        "POST /scforge/plugins/{id}/resubmit",
        "POST /scforge/plugins/{id}/versions",
        "POST /scforge/plugins/{pluginId}/comments",
        "POST /scforge/versions/{id}/file",
        "POST /scforge/versions/{id}/resubmit",
        "PUT /scforge/comments/{id}/vote/down",
        "PUT /scforge/comments/{id}/vote/up",
        "PUT /scforge/plugins/{id}",
        "PUT /scforge/plugins/{id}/vote/down",
        "PUT /scforge/plugins/{id}/vote/up",
    ];

    private static string ArtifactPath([CallerFilePath] string thisFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "docs", "openapi", "scforge-public.json"));

    private async Task<string> FetchRawAsync()
    {
        Client.DefaultRequestHeaders.Add("Accept", "application/json");
        var response = await Client.GetAsync(DocumentUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<JsonNode> FetchDocumentAsync() => JsonNode.Parse(await FetchRawAsync())!;

    private static string[] RoutesOf(JsonNode document)
    {
        var routes = new List<string>();
        foreach (var (path, item) in document["paths"]!.AsObject())
        {
            foreach (var (verb, _) in item!.AsObject())
            {
                if (verb is "get" or "put" or "post" or "delete" or "patch" or "options" or "head" or "trace")
                {
                    routes.Add(verb.ToUpperInvariant() + " " + path);
                }
            }
        }

        routes.Sort(StringComparer.Ordinal);
        return [.. routes];
    }

    [Fact]
    public async Task Public_document_lists_exactly_the_scforge_public_endpoints()
    {
        var document = await FetchDocumentAsync();
        Assert.Equal(ExpectedRoutes, RoutesOf(document));
    }

    [Fact]
    public async Task Public_document_excludes_admin_and_other_modules()
    {
        var document = await FetchDocumentAsync();
        var paths = document["paths"]!.AsObject().Select(p => p.Key).ToArray();
        Assert.NotEmpty(paths);
        Assert.All(paths, p => Assert.StartsWith("/scforge/", p, StringComparison.Ordinal));
        Assert.DoesNotContain(paths, p => p.StartsWith("/scforge/admin", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Public_document_declares_api_key_security_scheme()
    {
        var document = await FetchDocumentAsync();
        var scheme = document["components"]?["securitySchemes"]?["ScforgeApiKey"];
        Assert.NotNull(scheme);
        Assert.Equal("http", scheme!["type"]!.GetValue<string>());
        Assert.Equal("bearer", scheme["scheme"]!.GetValue<string>());
        Assert.Equal("scf_<base64url>", scheme["bearerFormat"]!.GetValue<string>());

        var security = document["security"]?.AsArray();
        Assert.NotNull(security);
        Assert.Contains(security!, entry => entry!.AsObject().ContainsKey("ScforgeApiKey"));
    }

    [Fact]
    public async Task Public_document_matches_committed_artifact()
    {
        var live = JsonNode.Parse(await FetchRawAsync());
        var path = ArtifactPath();

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var pretty = JsonSerializer.Serialize(live, new JsonSerializerOptions { WriteIndented = true })
                + Environment.NewLine;
            await File.WriteAllTextAsync(path, pretty, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail("已生成对外文档产物：" + path + "，请重新运行本测试确认全绿。");
        }

        var committed = JsonNode.Parse(await File.ReadAllTextAsync(path));
        Assert.True(
            JsonNode.DeepEquals(committed, live),
            "docs/openapi/scforge-public.json 与运行时文档不一致，请重新生成并提交：" + path);
    }
}
