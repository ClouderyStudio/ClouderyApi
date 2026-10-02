using System.Net;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// 试卷接口契约：公开视图剥离 answer/note、管理端 full 返回含答案的 EF 实体、
/// 评分算法（single/multiple/essay/judge、多选顺序无关、essay 不计正确）与 404/409 文案。
/// 路由 exam/ExamPapers 使用 MVC 默认 camelCase（不是 MHOP 的蛇形）。
/// </summary>
public sealed class ExamPapersContractTests : IntegrationTestBase
{
    private void SignInAsAdmin()
        => Client.DefaultRequestHeaders.Add(
            "Cookie",
            AuthCookie.CreateHeader(Factory.Services, AuthCookie.TestAdminCasdoorId));

    private static object BuildPaper(string id) => new
    {
        id,
        name = "契约测试试卷",
        sections = new[]
        {
            new
            {
                title = "第一部分",
                pointsPerQuestion = 2.0,
                questions = new object[]
                {
                    new { text = "单选", options = new[] { new { label = "A", text = "甲" }, new { label = "B", text = "乙" } }, answer = "A", note = "单选备注", type = "single" },
                    new { text = "多选", options = new[] { new { label = "A", text = "甲" }, new { label = "B", text = "乙" }, new { label = "C", text = "丙" } }, answer = "AC", note = "多选备注", type = "multiple" },
                    new { text = "作文", options = (object?)null, answer = "略", note = "作文备注", type = "essay" },
                    new { text = "判断", options = (object?)null, answer = "对", note = "判断备注", type = "judge" },
                },
            },
        },
        updatedAt = "2026-01-01T00:00:00Z",
    };

    private async Task CreatePaperAsync(string id)
    {
        var response = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/exam/ExamPapers", BuildPaper(id));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Public_view_hides_answers_while_full_view_exposes_them()
    {
        SignInAsAdmin();
        await CreatePaperAsync("paper-view");

        var (status, publicBody) = await JsonHttp.GetJsonAsync(Client, "/exam/ExamPapers/paper-view");
        Assert.Equal(HttpStatusCode.OK, status);
        var publicQuestion = publicBody.RootElement.GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.Equal(
            new[] { "text", "options", "type" },
            publicQuestion.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.False(publicQuestion.TryGetProperty("answer", out _));
        Assert.False(publicQuestion.TryGetProperty("note", out _));

        var full = await Client.GetAsync("/exam/ExamPapers/paper-view/full");
        Assert.Equal(HttpStatusCode.OK, full.StatusCode);
        var (_, fullBody) = await JsonHttp.ReadAsync(full);
        var fullQuestion = fullBody.RootElement.GetProperty("sections")[0].GetProperty("questions")[0];
        Assert.Equal("A", fullQuestion.GetProperty("answer").GetString());
        Assert.Equal("单选备注", fullQuestion.GetProperty("note").GetString());
    }

    [Fact]
    public async Task List_serializes_paper_view_in_declared_order()
    {
        SignInAsAdmin();
        await CreatePaperAsync("paper-list");

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/exam/ExamPapers");

        Assert.Equal(HttpStatusCode.OK, status);
        var item = body.RootElement.EnumerateArray().Single(e => e.GetProperty("id").GetString() == "paper-list");
        Assert.Equal(new[] { "id", "name", "sections" }, item.EnumerateObject().Select(p => p.Name).ToArray());
        var section = item.GetProperty("sections")[0];
        Assert.Equal(new[] { "title", "pointsPerQuestion", "questions" }, section.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(2d, section.GetProperty("pointsPerQuestion").GetDouble());
    }

    [Fact]
    public async Task Grade_mixed_answers_matches_documented_scoring()
    {
        SignInAsAdmin();
        await CreatePaperAsync("paper-grade");

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/exam/ExamPapers/paper-grade/grade", new
        {
            answers = new Dictionary<string, object?>
            {
                ["0-0"] = "A",
                ["0-1"] = new[] { "C", "A" },
                ["0-2"] = "我写的作文",
                ["0-3"] = "对",
            },
        });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(
            new[] { "totalCount", "scorableCount", "essayCount", "correctCount", "totalPoints", "earned", "accuracy", "results" },
            body.RootElement.EnumerateObject().Select(p => p.Name).ToArray());
        Assert.Equal(4, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, body.RootElement.GetProperty("scorableCount").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("essayCount").GetInt32());
        Assert.Equal(3, body.RootElement.GetProperty("correctCount").GetInt32());
        Assert.Equal(8, body.RootElement.GetProperty("totalPoints").GetInt32());
        Assert.Equal(6, body.RootElement.GetProperty("earned").GetInt32());
        Assert.Equal(100, body.RootElement.GetProperty("accuracy").GetInt32());

        var results = body.RootElement.GetProperty("results").EnumerateArray().ToArray();
        Assert.Equal(4, results.Length);
        Assert.Equal(
            new[] { "key", "type", "correct", "answered", "points", "earned", "standardAnswer", "note" },
            results[0].EnumerateObject().Select(p => p.Name).ToArray());
        Assert.True(results[0].GetProperty("correct").GetBoolean());
        Assert.True(results[1].GetProperty("correct").GetBoolean());
        Assert.False(results[2].GetProperty("correct").GetBoolean());
        Assert.Equal(0, results[2].GetProperty("earned").GetInt32());
        Assert.True(results[3].GetProperty("correct").GetBoolean());
    }

    [Fact]
    public async Task Grade_without_answers_reports_every_question_unanswered()
    {
        SignInAsAdmin();
        await CreatePaperAsync("paper-empty");

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/exam/ExamPapers/paper-empty/grade",
            new { answers = new Dictionary<string, object?>() });

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, body.RootElement.GetProperty("correctCount").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("earned").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("accuracy").GetInt32());
        foreach (var result in body.RootElement.GetProperty("results").EnumerateArray())
        {
            Assert.False(result.GetProperty("answered").GetBoolean());
            Assert.False(result.GetProperty("correct").GetBoolean());
        }
    }

    [Fact]
    public async Task Post_duplicate_id_returns_409_and_put_unknown_returns_404()
    {
        SignInAsAdmin();
        await CreatePaperAsync("paper-dup");

        var duplicate = await JsonHttp.SendAsync(Client, HttpMethod.Post, "/exam/ExamPapers", BuildPaper("paper-dup"));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var (_, duplicateBody) = await JsonHttp.ReadAsync(duplicate);
        Assert.Equal("试卷ID已存在", duplicateBody.RootElement.GetProperty("message").GetString());

        var put = await JsonHttp.SendAsync(Client, HttpMethod.Put, "/exam/ExamPapers/missing", BuildPaper("missing"));
        Assert.Equal(HttpStatusCode.NotFound, put.StatusCode);
        var (_, putBody) = await JsonHttp.ReadAsync(put);
        Assert.Equal("未找到该试卷", putBody.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Get_unknown_paper_returns_404_with_message()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/exam/ExamPapers/nope");
        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("未找到该试卷", body.RootElement.GetProperty("message").GetString());
    }
}
