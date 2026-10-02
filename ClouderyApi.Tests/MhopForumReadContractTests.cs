using System.Net;
using System.Text.Json;
using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// 公开板块的只读契约：板块列表、统计、帖子列表结构、post 详情、view_count 递增语义。
/// 这些字段名/类型/顺序是前端匿名浏览页依赖的红线。
/// </summary>
public sealed class MhopForumReadContractTests : IntegrationTestBase
{
    private static readonly string[] ExpectedSlugs = ["crisis", "mood", "stress", "relation", "sleep", "recovery", "chat"];

    private async Task<(int Id, JsonElement Item)> SeededPostAsync()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts?board=stress&size=50");
        Assert.Equal(HttpStatusCode.OK, status);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var item = Assert.Single(items);
        return (item.GetProperty("id").GetInt32(), item);
    }

    [Fact]
    public async Task Boards_returns_the_seven_slugs_in_order()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/boards");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        var slugs = body.RootElement.EnumerateArray().Select(b => b.GetProperty("slug").GetString()).ToArray();
        Assert.Equal(ExpectedSlugs, slugs);

        var first = body.RootElement.EnumerateArray().First();
        Assert.False(string.IsNullOrEmpty(first.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(first.GetProperty("color").GetString()));

        var stress = body.RootElement.EnumerateArray().Single(b => b.GetProperty("slug").GetString() == "stress");
        Assert.True(stress.GetProperty("count").GetInt32() >= 1);
    }

    [Fact]
    public async Task Stats_counts_only_visible_rows()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/stats");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.RootElement.GetProperty("posts").GetInt32() >= 1);
        Assert.True(body.RootElement.GetProperty("replies").GetInt32() >= 1);
        Assert.True(body.RootElement.GetProperty("users").GetInt32() >= 2);
    }

    [Fact]
    public async Task Posts_list_returns_masked_seeded_post_with_utc_timestamps()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts?board=stress&page=1&size=10");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.True(body.RootElement.GetProperty("total").GetInt32() >= 1);
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("size").GetInt32());

        var (_, item) = await SeededPostAsync();
        Assert.Equal("stress", item.GetProperty("board").GetString());
        Assert.Equal(1, item.GetProperty("status").GetInt32());
        Assert.True(item.GetProperty("is_anonymous").GetBoolean());
        Assert.Equal("匿名朋友", item.GetProperty("author").GetString());
        Assert.Equal(string.Empty, item.GetProperty("author_avatar").GetString());
        Assert.True(item.GetProperty("ai_replied").GetBoolean());
        Assert.True(item.GetProperty("reply_count").GetInt32() >= 1);
        Assert.Equal(JsonValueKind.Number, item.GetProperty("view_count").ValueKind);
        Assert.Equal(JsonValueKind.Array, item.GetProperty("images").ValueKind);
        Assert.False(item.GetProperty("mine").GetBoolean());
        Assert.EndsWith("Z", item.GetProperty("created_at").GetString());
        Assert.EndsWith("Z", item.GetProperty("last_reply_at").GetString());
    }

    [Fact]
    public async Task Post_detail_lists_the_ai_reply_first()
    {
        var (id, _) = await SeededPostAsync();

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts/" + id);

        Assert.Equal(HttpStatusCode.OK, status);
        var replies = body.RootElement.GetProperty("replies").EnumerateArray().ToArray();
        var reply = Assert.Single(replies);
        Assert.True(reply.GetProperty("is_ai").GetBoolean());
        Assert.Equal("AI 心理助手", reply.GetProperty("author").GetString());
        Assert.Equal(1, reply.GetProperty("status").GetInt32());
        Assert.Equal(id, reply.GetProperty("post_id").GetInt32());
        Assert.False(reply.GetProperty("recalled").GetBoolean());
        Assert.EndsWith("Z", reply.GetProperty("created_at").GetString());
    }

    [Fact]
    public async Task Post_detail_increments_view_count_only_for_truthy_inc_view()
    {
        var (id, _) = await SeededPostAsync();

        var (_, before) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts/" + id);
        var baseline = before.RootElement.GetProperty("view_count").GetInt32();

        var (_, one) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts/" + id + "?inc_view=1");
        Assert.Equal(baseline + 1, one.RootElement.GetProperty("view_count").GetInt32());

        var (_, two) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts/" + id + "?inc_view=2");
        Assert.Equal(baseline + 1, two.RootElement.GetProperty("view_count").GetInt32());

        var (_, upper) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts/" + id + "?inc_view=TRUE");
        Assert.Equal(baseline + 2, upper.RootElement.GetProperty("view_count").GetInt32());

        var (_, spaced) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts/" + id + "?inc_view=%20On%20");
        Assert.Equal(baseline + 3, spaced.RootElement.GetProperty("view_count").GetInt32());
    }

    [Fact]
    public async Task Post_detail_hides_pending_posts()
    {
        int pendingId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var post = new MhopPost
            {
                UserId = null,
                Content = "等待巡检的帖子",
                Board = "mood",
                Status = 0,
                IsAnonymous = true,
                CreatedAt = DateTime.UtcNow,
            };
            db.MhopPosts.Add(post);
            await db.SaveChangesAsync();
            pendingId = post.Id;
        }

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts/" + pendingId);

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("帖子不存在或正在审核中", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Posts_with_unknown_board_returns_400()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/posts?board=not-a-board");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("板块不存在", body.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Likes_mine_is_empty_for_anonymous()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/likes/mine?target_type=post");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(0, body.RootElement.GetProperty("ids").GetArrayLength());
    }

    [Fact]
    public async Task Mine_summary_requires_a_bearer_token()
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/forum/mine/summary");

        Assert.Equal(HttpStatusCode.Unauthorized, status);
        Assert.Equal("请先登录", body.RootElement.GetProperty("detail").GetString());
    }
}
