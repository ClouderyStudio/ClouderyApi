using System.Net;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// 身份 Cookie 方案的鉴权契约：只挂 [AdminOnly] 的路由未登录给 401 JSON（过滤器自己写响应），
/// 登录但不是管理员给 403；已在白名单的管理员放行。用真实 Cookies 方案加密的票据模拟登录态。
/// </summary>
public sealed class CookieAuthorizationContractTests : IntegrationTestBase
{
    private void SignIn(string? casdoorId)
        => Client.DefaultRequestHeaders.Add("Cookie", AuthCookie.CreateHeader(Factory.Services, casdoorId));

    [Fact]
    public async Task Whitelists_rejects_signed_in_non_admin_with_403()
    {
        SignIn("someone-else");

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/zhuxs/whitelists");

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("无管理员权限，操作被拒绝", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Whitelists_allows_configured_admin()
    {
        SignIn(AuthCookie.TestAdminCasdoorId);

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/zhuxs/whitelists");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
    }

    [Fact]
    public async Task Exam_paper_full_requires_login_then_admin()
    {
        var (anonymous, anonymousBody) = await JsonHttp.GetJsonAsync(Client, "/exam/ExamPapers/contract-paper/full");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous);
        Assert.Equal("请先登录", anonymousBody.RootElement.GetProperty("message").GetString());

        SignIn("someone-else");
        var (forbidden, forbiddenBody) = await JsonHttp.GetJsonAsync(Client, "/exam/ExamPapers/contract-paper/full");
        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
        Assert.Equal("无管理员权限，操作被拒绝", forbiddenBody.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Admin_can_create_paper_and_public_view_hides_answers()
    {
        SignIn(AuthCookie.TestAdminCasdoorId);

        var (created, createdBody) = await JsonHttp.PostJsonAsync(Client, "/exam/ExamPapers", new
        {
            name = "契约试卷",
            sections = new[]
            {
                new
                {
                    title = "第一节",
                    pointsPerQuestion = 5.0,
                    questions = new[]
                    {
                        new
                        {
                            text = "1+1=?",
                            options = new[] { new { label = "A", text = "1" }, new { label = "B", text = "2" } },
                            answer = "B",
                            note = "内部备注",
                            type = "single",
                        },
                    },
                },
            },
        });
        Assert.Equal(HttpStatusCode.Created, created);
        var paperId = createdBody.RootElement.GetProperty("id").GetString()!;

        var (fullStatus, full) = await JsonHttp.GetJsonAsync(Client, $"/exam/ExamPapers/{paperId}/full");
        Assert.Equal(HttpStatusCode.OK, fullStatus);
        Assert.Equal("B", full.RootElement.GetProperty("sections")[0].GetProperty("questions")[0].GetProperty("answer").GetString());

        var (publicStatus, view) = await JsonHttp.GetJsonAsync(Client, $"/exam/ExamPapers/{paperId}");
        Assert.Equal(HttpStatusCode.OK, publicStatus);
        var question = view.RootElement.GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.False(question.TryGetProperty("answer", out _));
        Assert.False(question.TryGetProperty("note", out _));
        Assert.Equal("single", question.GetProperty("type").GetString());
    }
}
