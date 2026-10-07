using System.Net;
using System.Text.Json;
using ClouderyApi.Tests.TestSupport;

namespace ClouderyApi.Tests;

/// <summary>
/// 发帖/回帖契约：鉴权、手机号 + 邮箱验证码双重门槛、201 状态码、敏感词命中后置为待复核（status=2）。
/// </summary>
public sealed class MhopForumWriteContractTests : IntegrationTestBase
{
    private async Task<string> RegisterAsync(string username)
    {
        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/auth/register",
            new { username, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, status);
        return body.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<string> RegisterWithPhoneAsync(string username, string phone)
    {
        var token = await RegisterAsync(username);
        var (status, _) = await JsonHttp.PutJsonAsync(Client, "/mhop/auth/me/phone", new { phone }, token);
        Assert.Equal(HttpStatusCode.OK, status);
        return token;
    }

    /// <summary>注册 → 绑手机 → 验证邮箱：满足发帖 / 回帖的全部门槛。</summary>
    private async Task<string> RegisterEligibleAsync(string username, string phone, string email)
    {
        var token = await RegisterWithPhoneAsync(username, phone);
        await MhopEmailVerify.VerifyEmailAsync(Factory, Client, token, email);
        return token;
    }

    [Fact]
    public async Task Creating_post_requires_bearer_token()
    {
        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/posts",
            new { content = "你好", board = "stress", is_anonymous = true });

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("请先登录", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Creating_post_requires_a_bound_phone()
    {
        var token = await RegisterAsync("forum_nophone");

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/posts",
            new { content = "你好", board = "stress", is_anonymous = true }, token);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("发帖前请先在个人主页绑定手机号", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Creating_post_requires_a_verified_email()
    {
        // 手机号门槛先判定：只绑手机号、未验证邮箱时被邮箱门槛拦下
        var token = await RegisterWithPhoneAsync("forum_noemail", "13700137005");

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/posts",
            new { content = "你好", board = "stress", is_anonymous = true }, token);

        Assert.Equal(HttpStatusCode.Forbidden, status);
        Assert.Equal("请先在个人主页绑定邮箱并完成邮箱验证", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Creating_post_validates_content_and_board()
    {
        var token = await RegisterEligibleAsync("forum_invalid", "13700137001", "forum_invalid@example.com");

        var (empty, emptyBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/posts",
            new { content = "   ", board = "stress" }, token);
        Assert.Equal(HttpStatusCode.BadRequest, empty);
        Assert.Equal("内容不能为空", emptyBody.RootElement.GetProperty("detail").GetString());

        var (badBoard, badBoardBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/posts",
            new { content = "内容", board = "not-a-board" }, token);
        Assert.Equal(HttpStatusCode.BadRequest, badBoard);
        Assert.Equal("请选择板块", badBoardBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Created_post_is_pending_and_masked_then_sensitive_reply_is_held_for_review()
    {
        var token = await RegisterEligibleAsync("forum_flow", "13700137002", "forum_flow@example.com");

        var (created, createdBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/posts",
            new { content = "最近总是睡不好，想找人说说话。", board = "stress", is_anonymous = true }, token);
        Assert.Equal(HttpStatusCode.Created, created);
        var post = createdBody.RootElement;
        Assert.Equal(0, post.GetProperty("status").GetInt32());
        Assert.Equal("匿名朋友", post.GetProperty("author").GetString());
        Assert.True(post.GetProperty("mine").GetBoolean());
        var postId = post.GetProperty("id").GetInt32();

        var (reply, replyBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/posts/" + postId + "/replies",
            new { content = "我也是，抱抱你。", is_anonymous = true }, token);
        Assert.Equal(HttpStatusCode.Created, reply);
        Assert.Equal(0, replyBody.RootElement.GetProperty("status").GetInt32());

        var (held, heldBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/posts/" + postId + "/replies",
            new { content = "刷单兼职了解一下", is_anonymous = true }, token);
        Assert.Equal(HttpStatusCode.Created, held);
        Assert.Equal(2, heldBody.RootElement.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Replying_to_a_missing_post_returns_404()
    {
        var token = await RegisterEligibleAsync("forum_missing", "13700137003", "forum_missing@example.com");

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/posts/987654/replies",
            new { content = "在吗", is_anonymous = true }, token);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("帖子不存在或已被移除", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Like_toggle_rejects_unknown_target_type()
    {
        var token = await RegisterEligibleAsync("forum_like", "13700137004", "forum_like@example.com");

        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/forum/likes/toggle",
            new { target_type = "bogus", target_id = 1 }, token);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("非法点赞对象", body.RootElement.GetProperty("detail").GetString());
    }
}
