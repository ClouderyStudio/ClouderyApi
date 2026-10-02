using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ClouderyApi.Tests;

/// <summary>
/// 路由表快照：直接 GET 运行时 swagger.json（Development 下 UseSwagger 生效，
/// 且 swagger 不是 MVC endpoint，不受 [Authorize]/[AdminOnly] 影响），
/// 把 paths 规范化成 "METHOD /path" 排序文本与入库基线逐行比对。
/// Stage 2 抽取应用层时，任何路由/动词漂移都会在这里立刻暴露。
/// 基线文件缺失时会写出当前路由表并 Assert.Fail（首次生成后重跑即绿）。
/// </summary>
public sealed class SwaggerRouteSnapshotTests : IntegrationTestBase
{
    private static string SnapshotPath([CallerFilePath] string thisFile = "")
        => Path.Combine(Path.GetDirectoryName(thisFile)!, "TestData", "swagger-routes.snapshot.txt");

    private async Task<string> FetchCanonicalRoutesAsync()
    {
        var response = await Client.GetAsync("/swagger/v1/swagger.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(document.RootElement.TryGetProperty("paths", out var paths));

        var routes = new List<string>();
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject())
            {
                if (operation.Name is "get" or "put" or "post" or "delete" or "patch" or "options" or "head" or "trace")
                {
                    routes.Add(operation.Name.ToUpperInvariant() + " " + path.Name);
                }
            }
        }

        routes.Sort(StringComparer.Ordinal);
        return string.Join("\n", routes) + "\n";
    }

    [Fact]
    public async Task Route_table_contains_registered_endpoints()
    {
        Client.DefaultRequestHeaders.Add("Accept", "application/json");
        var canonical = await FetchCanonicalRoutesAsync();

        string[] expected =
        [
            "DELETE /cloudery/Members/{id}",
            "GET /cloudery/Members",
            "GET /cloudery/Members/{id}",
            "POST /cloudery/Members",
            "PUT /cloudery/Members/{id}",
            "GET /exam/ExamPapers",
            "GET /exam/ExamPapers/{id}",
            "GET /exam/ExamPapers/{id}/full",
            "POST /exam/ExamPapers",
            "POST /exam/ExamPapers/{id}/grade",
            "PUT /exam/ExamPapers/{id}",
            "DELETE /exam/ExamPapers/{id}",
            "GET /exam/results",
            "POST /exam/results",
            "POST /exam/results/sync",
            "DELETE /exam/results",
            "DELETE /exam/results/{id}",
            "POST /exam/result-analysis",
            "GET /identity/Auth/config",
            "GET /identity/Auth/state",
            "POST /identity/Auth/callback",
            "GET /identity/Auth/me",
            "POST /identity/Auth/logout",
            "GET /identity/Auth/status",
            "GET /zhuxs/Terms",
            "GET /zhuxs/Terms/{id}",
            "POST /zhuxs/Terms",
            "PUT /zhuxs/Terms/{id}",
            "DELETE /zhuxs/Terms/{id}",
            "GET /zhuxs/Applications",
            "GET /zhuxs/Applications/{id}",
            "POST /zhuxs/Applications",
            "PUT /zhuxs/Applications/{id}",
            "DELETE /zhuxs/Applications/{id}",
            "GET /zhuxs/Whitelists",
            "GET /zhuxs/Whitelists/{id}",
            "POST /zhuxs/Whitelists",
            "DELETE /zhuxs/Whitelists/{id}",
        ];

        var actual = canonical.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(e => !actual.Contains(e)).ToArray();
        Assert.True(missing.Length == 0, "swagger 缺失以下契约端点：" + string.Join(", ", missing));
    }

    [Fact]
    public async Task Route_table_matches_committed_snapshot()
    {
        var canonical = await FetchCanonicalRoutesAsync();
        var path = SnapshotPath();

        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(
                path,
                "# ClouderyApi 运行时路由快照（swagger.json 的 METHOD + path，Ordinal 排序）\n"
                + "# 生成方式：dotnet test --filter FullyQualifiedName~SwaggerRouteSnapshotTests\n"
                + canonical,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail("已生成路由快照基线：" + path + "，请重新运行本测试确认全绿。");
        }

        var committed = (await File.ReadAllTextAsync(path)).Replace("\r\n", "\n");
        var committedBody = string.Join(
            "\n",
            committed.Split('\n').Where(line => !line.StartsWith('#')).Where(line => line.Length > 0)) + "\n";

        Assert.Equal(committedBody, canonical);
    }
}
