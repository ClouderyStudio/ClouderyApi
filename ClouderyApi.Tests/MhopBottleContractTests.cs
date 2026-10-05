using System.Net;
using System.Text.Json;
using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Shared.Ai;
using ClouderyApi.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// 漂流瓶**前台** HTTP 契约（docs/DDD-REFACTOR-PLAN.md 附录 C 第 6 项点名的覆盖缺口）。
/// 固化：路由全部要求登录、响应字段名与类型、匿名红线（只下发「是不是我」，绝不含对方身份）、
/// 状态机（Pending/Drifting/Picked/Ended/Removed）与错误形状（404/409/422/429）。
/// 任何重构都不得改变这里的断言。
/// </summary>
public sealed class MhopBottleContractTests : IntegrationTestBase
{
    private async Task<(string Token, int UserId)> RegisterAsync(string username)
    {
        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/auth/register",
            new { username, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, status);
        var token = body.RootElement.GetProperty("access_token").GetString()!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var user = await db.MhopUsers.AsNoTracking().SingleAsync(u => u.Username == username);
        return (token, user.Id);
    }

    /// <summary>直接落库种一个瓶子（不走 HTTP 投瓶，避免触发异步 AI 初筛，保证断言确定性）。</summary>
    private async Task<int> SeedBottleAsync(
        int throwerId,
        int status,
        string content = "海里的瓶子",
        bool crisis = false,
        string aiFlag = "",
        int reportedCount = 0)
    {
        var now = DateTime.UtcNow;
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var bottle = new MhopBottle
        {
            UserId = throwerId,
            Content = content,
            Status = status,
            Crisis = crisis,
            AiFlag = aiFlag,
            ReportedCount = reportedCount,
            LastMessageAt = now,
            CreatedAt = now,
            ThrowerLastReadAt = now,
        };
        db.MhopBottles.Add(bottle);
        await db.SaveChangesAsync();
        return bottle.Id;
    }

    private async Task<int> SeedMessageAsync(int bottleId, int senderId, string content, bool hidden = false, string aiFlag = "")
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var message = new MhopBottleMessage
        {
            BottleId = bottleId,
            SenderUserId = senderId,
            Content = content,
            Status = hidden ? MhopBottleMessageStatus.Hidden : MhopBottleMessageStatus.Visible,
            AiFlag = aiFlag,
            CreatedAt = DateTime.UtcNow,
        };
        db.MhopBottleMessages.Add(message);
        await db.SaveChangesAsync();
        return message.Id;
    }

    private static void AssertNoIdentityFields(JsonElement element)
    {
        foreach (var leak in new[] { "user_id", "thrower_id", "picker_id", "sender_id", "sender_name", "username" })
            Assert.False(element.TryGetProperty(leak, out _), $"前台响应不得下发身份字段 {leak}");
    }

    [Fact]
    public async Task All_bottle_endpoints_require_login_with_mhop_error_shape()
    {
        (HttpMethod Method, string Path, object? Body)[] cases =
        [
            (HttpMethod.Post, "/mhop/bottles", new { content = "你好" }),
            (HttpMethod.Post, "/mhop/bottles/pick", null),
            (HttpMethod.Get, "/mhop/bottles/mine", null),
            (HttpMethod.Get, "/mhop/bottles/sea/count", null),
            (HttpMethod.Get, "/mhop/bottles/1", null),
            (HttpMethod.Post, "/mhop/bottles/1/messages", new { content = "你好" }),
            (HttpMethod.Post, "/mhop/bottles/1/end", null),
            (HttpMethod.Post, "/mhop/bottles/1/report", new { reason = "广告" }),
        ];

        foreach (var (method, path, payload) in cases)
        {
            var (status, body) = await JsonHttp.ReadAsync(await JsonHttp.SendAsync(Client, method, path, payload));
            Assert.Equal(HttpStatusCode.Unauthorized, status);
            Assert.Equal("请先登录", body.RootElement.GetProperty("detail").GetString());
        }
    }

    [Fact]
    public async Task Throw_returns_bottle_shape_with_hotline_only_for_crisis_content()
    {
        var (token, _) = await RegisterAsync("bottle_thrower");

        var (ok, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles", new { content = "今天有点累，但还好" }, token);
        Assert.Equal(HttpStatusCode.OK, ok);
        Assert.True(body.RootElement.GetProperty("id").GetInt32() > 0);
        Assert.Equal(JsonValueKind.Number, body.RootElement.GetProperty("status").ValueKind);
        Assert.False(body.RootElement.GetProperty("crisis").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("hotline").ValueKind);

        var (crisis, crisisBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles",
            new { content = "我最近总想着自杀，撑不下去了" }, token);
        Assert.Equal(HttpStatusCode.OK, crisis);
        Assert.True(crisisBody.RootElement.GetProperty("crisis").GetBoolean());
        Assert.Equal(CrisisSupport.HotlineNumber, crisisBody.RootElement.GetProperty("hotline").GetString());

        var (sensitive, sensitiveBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles",
            new { content = "加微信领红包" }, token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, sensitive);
        Assert.Equal("内容可能包含不当或违规信息，请修改后再扔出", sensitiveBody.RootElement.GetProperty("detail").GetString());

        var (empty, emptyBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles", new { content = "   " }, token);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, empty);
        Assert.Equal("瓶子内容需为 1-500 字", emptyBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Mine_returns_limits_and_rows_without_identity_fields()
    {
        var (token, userId) = await RegisterAsync("bottle_mine");
        await SeedBottleAsync(userId, MhopBottleStatus.Pending, "还没过审的瓶子");

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/mine", token);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(3, body.RootElement.GetProperty("throw_limit").GetInt32());
        Assert.Equal(10, body.RootElement.GetProperty("pick_limit").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("thrown_today").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("picked_today").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("sea_count").GetInt32());

        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        var item = Assert.Single(items);
        Assert.Equal("还没过审的瓶子", item.GetProperty("preview").GetString());
        Assert.Equal("thrower", item.GetProperty("role").GetString());
        Assert.Equal(0, item.GetProperty("status").GetInt32());
        Assert.False(item.GetProperty("crisis").GetBoolean());
        Assert.Equal(0, item.GetProperty("unread").GetInt32());
        Assert.Equal(0, item.GetProperty("message_count").GetInt32());
        Assert.Equal("还没过审的瓶子", item.GetProperty("last_message").GetString());
        Assert.EndsWith("Z", item.GetProperty("created_at").GetString());
        Assert.EndsWith("Z", item.GetProperty("last_message_at").GetString());
        AssertNoIdentityFields(item);
    }

    [Fact]
    public async Task Pick_returns_409_when_sea_is_empty_even_with_own_bottles()
    {
        var (pickerToken, pickerId) = await RegisterAsync("bottle_picker_empty");
        await SeedBottleAsync(pickerId, MhopBottleStatus.Drifting, "自己扔的瓶子");

        var (sea, seaBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/sea/count", pickerToken);
        Assert.Equal(HttpStatusCode.OK, sea);
        Assert.Equal(0, seaBody.RootElement.GetProperty("count").GetInt32());

        var (pick, pickBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/pick", new { }, pickerToken);
        Assert.Equal(HttpStatusCode.Conflict, pick);
        Assert.Equal("海里暂时没有漂着的瓶子，先扔一个，稍后再来捞捞看吧", pickBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Pick_claims_a_drifting_bottle_and_details_hide_identity()
    {
        var (pickerToken, _) = await RegisterAsync("bottle_picker");
        var (intruderToken, _) = await RegisterAsync("bottle_intruder");
        const int throwerId = 4242;
        var bottleId = await SeedBottleAsync(throwerId, MhopBottleStatus.Drifting, "海里的秘密");

        var (before, beforeBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/sea/count", pickerToken);
        Assert.Equal(HttpStatusCode.OK, before);
        Assert.Equal(1, beforeBody.RootElement.GetProperty("count").GetInt32());

        var (pick, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/pick", new { }, pickerToken);
        Assert.Equal(HttpStatusCode.OK, pick);
        Assert.Equal(bottleId, body.RootElement.GetProperty("id").GetInt32());
        Assert.Equal("海里的秘密", body.RootElement.GetProperty("content").GetString());
        Assert.Equal(MhopBottleStatus.Picked, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("picker", body.RootElement.GetProperty("role").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("unread").GetInt32());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("end_reason").ValueKind);
        Assert.False(body.RootElement.GetProperty("ended_by_me").GetBoolean());
        Assert.Empty(body.RootElement.GetProperty("messages").EnumerateArray());
        Assert.EndsWith("Z", body.RootElement.GetProperty("picked_at").GetString());
        AssertNoIdentityFields(body.RootElement);

        var (afterSea, afterSeaBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/sea/count", pickerToken);
        Assert.Equal(HttpStatusCode.OK, afterSea);
        Assert.Equal(0, afterSeaBody.RootElement.GetProperty("count").GetInt32());

        var (again, againBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/pick", new { }, pickerToken);
        Assert.Equal(HttpStatusCode.Conflict, again);
        Assert.Equal("海里暂时没有漂着的瓶子，先扔一个，稍后再来捞捞看吧", againBody.RootElement.GetProperty("detail").GetString());

        // 非会话双方：一律 404，不泄露存在性
        var (intruder, intruderBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/" + bottleId, intruderToken);
        Assert.Equal(HttpStatusCode.NotFound, intruder);
        Assert.Equal("会话不存在", intruderBody.RootElement.GetProperty("detail").GetString());

        var (unknown, unknownBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/987654", pickerToken);
        Assert.Equal(HttpStatusCode.NotFound, unknown);
        Assert.Equal("会话不存在", unknownBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Conversation_flow_tracks_unread_after_id_and_end_state()
    {
        var (throwerToken, throwerId) = await RegisterAsync("bottle_conv_thrower");
        var (pickerToken, _) = await RegisterAsync("bottle_conv_picker");
        var bottleId = await SeedBottleAsync(throwerId, MhopBottleStatus.Drifting, "海里的秘密");

        // 捞起前不能举报
        var (tooEarly, tooEarlyBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/" + bottleId + "/report",
            new { reason = "广告" }, throwerToken);
        Assert.Equal(HttpStatusCode.Conflict, tooEarly);
        Assert.Equal("瓶子还未被捞起，暂不能举报", tooEarlyBody.RootElement.GetProperty("detail").GetString());

        var (pick, _) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/pick", new { }, pickerToken);
        Assert.Equal(HttpStatusCode.OK, pick);

        var (sent, sentBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/" + bottleId + "/messages",
            new { content = "你好呀" }, pickerToken);
        Assert.Equal(HttpStatusCode.OK, sent);
        var messageId = sentBody.RootElement.GetProperty("id").GetInt32();
        Assert.True(sentBody.RootElement.GetProperty("mine").GetBoolean());
        Assert.Equal("你好呀", sentBody.RootElement.GetProperty("content").GetString());
        Assert.Equal(1, sentBody.RootElement.GetProperty("status").GetInt32());
        Assert.False(sentBody.RootElement.GetProperty("crisis").GetBoolean());
        Assert.EndsWith("Z", sentBody.RootElement.GetProperty("created_at").GetString());

        var (detail, detailBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/" + bottleId, throwerToken);
        Assert.Equal(HttpStatusCode.OK, detail);
        Assert.Equal(1, detailBody.RootElement.GetProperty("unread").GetInt32());
        Assert.Equal("thrower", detailBody.RootElement.GetProperty("role").GetString());
        var received = Assert.Single(detailBody.RootElement.GetProperty("messages").EnumerateArray());
        Assert.False(received.GetProperty("mine").GetBoolean());
        AssertNoIdentityFields(received);

        // 已读回写：再次拉取未读归零
        var (_, reread) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/" + bottleId, throwerToken);
        Assert.Equal(0, reread.RootElement.GetProperty("unread").GetInt32());

        // after_id 增量轮询只返回更新的消息
        var (_, incremental) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/" + bottleId + "?after_id=" + messageId, throwerToken);
        Assert.Empty(incremental.RootElement.GetProperty("messages").EnumerateArray());

        // 长度上限
        var (tooLong, tooLongBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/" + bottleId + "/messages",
            new { content = new string('x', 1001) }, pickerToken);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tooLong);
        Assert.Equal("消息需为 1-1000 字", tooLongBody.RootElement.GetProperty("detail").GetString());

        // 双方各可举报一次（幂等，不改变响应形状）
        var (report1, report1Body) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/" + bottleId + "/report",
            new { reason = "广告骚扰" }, pickerToken);
        Assert.Equal(HttpStatusCode.OK, report1);
        Assert.True(report1Body.RootElement.GetProperty("success").GetBoolean());
        var (report2, report2Body) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/" + bottleId + "/report",
            new { reason = "重复举报" }, throwerToken);
        Assert.Equal(HttpStatusCode.OK, report2);
        Assert.True(report2Body.RootElement.GetProperty("success").GetBoolean());

        // 结束（幂等），之后发送消息被拒
        var (end, endBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/" + bottleId + "/end", new { }, pickerToken);
        Assert.Equal(HttpStatusCode.OK, end);
        Assert.True(endBody.RootElement.GetProperty("success").GetBoolean());
        var (endAgain, endAgainBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/" + bottleId + "/end", new { }, pickerToken);
        Assert.Equal(HttpStatusCode.OK, endAgain);
        Assert.True(endAgainBody.RootElement.GetProperty("success").GetBoolean());

        var (_, ended) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/" + bottleId, pickerToken);
        Assert.Equal(MhopBottleStatus.Ended, ended.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(0, ended.RootElement.GetProperty("end_reason").GetInt32());
        Assert.True(ended.RootElement.GetProperty("ended_by_me").GetBoolean());

        var (rejected, rejectedBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/" + bottleId + "/messages",
            new { content = "还在吗" }, pickerToken);
        Assert.Equal(HttpStatusCode.Conflict, rejected);
        Assert.Equal("对话已经结束", rejectedBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Hidden_messages_are_not_delivered_and_pending_conversation_is_not_pickable()
    {
        var (throwerToken, throwerId) = await RegisterAsync("bottle_hidden_thrower");
        var (pickerToken, pickerId) = await RegisterAsync("bottle_hidden_picker");
        var bottleId = await SeedBottleAsync(throwerId, MhopBottleStatus.Picked, "已经开始的对话");
        await SeedMessageAsync(bottleId, throwerId, "可见消息");
        await SeedMessageAsync(bottleId, pickerId, "被隐藏的消息", hidden: true);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var bottle = await db.MhopBottles.SingleAsync(b => b.Id == bottleId);
            bottle.PickerUserId = pickerId;
            bottle.PickedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/" + bottleId, pickerToken);
        Assert.Equal(HttpStatusCode.OK, status);
        var messages = body.RootElement.GetProperty("messages").EnumerateArray().ToArray();
        var visible = Assert.Single(messages);
        Assert.Equal("可见消息", visible.GetProperty("content").GetString());

        // 未开始（待审核）的瓶子不能发消息
        var pendingId = await SeedBottleAsync(throwerId, MhopBottleStatus.Pending, "等待审核");
        var (rejected, rejectedBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/bottles/" + pendingId + "/messages",
            new { content = "在吗" }, throwerToken);
        Assert.Equal(HttpStatusCode.Conflict, rejected);
        Assert.Equal("对话已经结束", rejectedBody.RootElement.GetProperty("detail").GetString());
    }
}
