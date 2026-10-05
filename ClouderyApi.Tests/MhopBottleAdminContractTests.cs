using System.Net;
using System.Text.Json;
using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Tests;

/// <summary>
/// 漂流瓶**后台审核** HTTP 契约（docs/DDD-REFACTOR-PLAN.md 附录 C 第 6 项点名的覆盖缺口）。
/// 固化：鉴权阶梯（401/403/200）、统计口径、审核队列排序与分页夹取、
/// 身份字段仅在后台出现、下架/恢复/放行与消息隐藏/恢复的状态推导、以及 404/409 文案。
/// </summary>
public sealed class MhopBottleAdminContractTests : IntegrationTestBase
{
    private async Task<(string Token, int UserId, string Username)> RegisterAsync(string username)
    {
        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/auth/register",
            new { username, password = "secret123" });
        Assert.Equal(HttpStatusCode.OK, status);
        var token = body.RootElement.GetProperty("access_token").GetString()!;

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
        var user = await db.MhopUsers.AsNoTracking().SingleAsync(u => u.Username == username);
        return (token, user.Id, username);
    }

    private async Task<string> AdminTokenAsync()
    {
        var (status, body) = await JsonHttp.PostJsonAsync(Client, "/mhop/auth/login", new
        {
            username = ClouderyApiFactory.TestSeedAdminUsername,
            password = ClouderyApiFactory.TestSeedAdminPassword,
        });
        Assert.Equal(HttpStatusCode.OK, status);
        return body.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>直接落库种一个瓶子（不经 HTTP 投瓶，避免触发异步 AI 初筛）。</summary>
    private async Task<int> SeedBottleAsync(
        int throwerId,
        int status,
        string content = "后台审核用瓶子",
        bool crisis = false,
        string aiFlag = "",
        int reportedCount = 0,
        int? pickerId = null)
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
            PickerUserId = pickerId,
            PickedAt = pickerId is null ? null : now,
            LastMessageAt = now,
            CreatedAt = now,
            ThrowerLastReadAt = now,
            PickerLastReadAt = pickerId is null ? null : now,
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

    private async Task<JsonDocument> AdminDetailAsync(string token, int bottleId)
    {
        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles/" + bottleId, token);
        Assert.Equal(HttpStatusCode.OK, status);
        return body;
    }

    [Fact]
    public async Task Admin_bottle_endpoints_enforce_login_then_permission()
    {
        var (stats, statsBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles/stats");
        Assert.Equal(HttpStatusCode.Unauthorized, stats);
        Assert.Equal("请先登录", statsBody.RootElement.GetProperty("detail").GetString());

        var (userToken, _, _) = await RegisterAsync("bottle_admin_plain");
        var (forbidden, forbiddenBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles/stats", userToken);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden);
        Assert.Equal("需要管理员权限", forbiddenBody.RootElement.GetProperty("detail").GetString());

        var (deniedList, _) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles", userToken);
        Assert.Equal(HttpStatusCode.Forbidden, deniedList);

        var admin = await AdminTokenAsync();
        var (ok, _) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles/stats", admin);
        Assert.Equal(HttpStatusCode.OK, ok);
    }

    [Fact]
    public async Task Stats_counts_each_review_bucket_exactly()
    {
        var admin = await AdminTokenAsync();
        const int throwerId = 7001;

        await SeedBottleAsync(throwerId, MhopBottleStatus.Pending, "等待审核");
        await SeedBottleAsync(throwerId, MhopBottleStatus.Pending, "AI 没结论",
            aiFlag: MhopModerationOutcome.Unavailable);
        await SeedBottleAsync(throwerId, MhopBottleStatus.Drifting, "危机内容", crisis: true);
        await SeedBottleAsync(throwerId, MhopBottleStatus.Drifting, "疑似违规",
            aiFlag: MhopModerationOutcome.Suspect);
        await SeedBottleAsync(throwerId, MhopBottleStatus.Drifting, "被举报", reportedCount: 1);
        var withMessages = await SeedBottleAsync(throwerId, MhopBottleStatus.Picked, "带消息", pickerId: 7002);
        await SeedMessageAsync(withMessages, throwerId, "已被隐藏", hidden: true);
        await SeedMessageAsync(withMessages, 7002, "疑似消息", aiFlag: MhopModerationOutcome.Suspect);

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles/stats", admin);
        Assert.Equal(HttpStatusCode.OK, status);
        var stats = body.RootElement;
        Assert.Equal(1, stats.GetProperty("crisis").GetInt32());
        Assert.Equal(1, stats.GetProperty("suspect").GetInt32());
        Assert.Equal(1, stats.GetProperty("reported").GetInt32());
        Assert.Equal(2, stats.GetProperty("pending").GetInt32());
        Assert.Equal(1, stats.GetProperty("ai_unavailable").GetInt32());
        Assert.Equal(1, stats.GetProperty("flagged_messages").GetInt32());
        Assert.Equal(1, stats.GetProperty("hidden_messages").GetInt32());
    }

    [Fact]
    public async Task List_pages_pending_first_and_reveals_identity_only_in_admin()
    {
        var admin = await AdminTokenAsync();
        var (_, throwerId, throwerName) = await RegisterAsync("bottle_admin_thrower");
        var (_, pickerId, pickerName) = await RegisterAsync("bottle_admin_picker");

        var pendingId = await SeedBottleAsync(throwerId, MhopBottleStatus.Pending, "待审的瓶子");
        var pickedId = await SeedBottleAsync(throwerId, MhopBottleStatus.Picked, "被举报的对话",
            reportedCount: 2, pickerId: pickerId);

        var (status, body) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles", admin);
        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(2, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(20, body.RootElement.GetProperty("size").GetInt32());

        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, items.Length);
        // 审核队列排序：待审优先，其次被举报
        Assert.Equal(pendingId, items[0].GetProperty("id").GetInt32());
        Assert.Equal(MhopBottleStatus.Pending, items[0].GetProperty("status").GetInt32());
        Assert.Equal(throwerId, items[0].GetProperty("thrower_id").GetInt32());
        Assert.Equal(throwerName, items[0].GetProperty("thrower_name").GetString());
        Assert.Equal(JsonValueKind.Null, items[0].GetProperty("picker_id").ValueKind);
        Assert.Equal(0, items[0].GetProperty("message_count").GetInt32());

        Assert.Equal(pickedId, items[1].GetProperty("id").GetInt32());
        Assert.Equal(2, items[1].GetProperty("reported_count").GetInt32());
        Assert.Equal(pickerId, items[1].GetProperty("picker_id").GetInt32());
        Assert.Equal(pickerName, items[1].GetProperty("picker_name").GetString());

        // 分页参数夹取 + 过滤
        var (_, clamped) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles?page=0&size=999", admin);
        Assert.Equal(1, clamped.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(100, clamped.RootElement.GetProperty("size").GetInt32());
        Assert.Equal(2, clamped.RootElement.GetProperty("total").GetInt32());

        var (_, byStatus) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles?status=2", admin);
        Assert.Equal(1, byStatus.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(pickedId, byStatus.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());

        var (_, byReported) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles?reported=true", admin);
        Assert.Equal(1, byReported.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(pickedId, byReported.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());

        var (_, byFlag) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles?flag=pending", admin);
        Assert.Equal(1, byFlag.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(pendingId, byFlag.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Detail_returns_all_messages_but_counts_only_visible_ones()
    {
        var admin = await AdminTokenAsync();
        var (_, throwerId, throwerName) = await RegisterAsync("bottle_detail_thrower");
        var (_, pickerId, pickerName) = await RegisterAsync("bottle_detail_picker");
        var bottleId = await SeedBottleAsync(throwerId, MhopBottleStatus.Picked, "有历史消息的对话",
            pickerId: pickerId, reportedCount: 1);
        var first = await SeedMessageAsync(bottleId, throwerId, "第一条");
        var hidden = await SeedMessageAsync(bottleId, pickerId, "被隐藏的一条", hidden: true);
        var third = await SeedMessageAsync(bottleId, pickerId, "第三条");

        var body = await AdminDetailAsync(admin, bottleId);
        var detail = body.RootElement;
        Assert.Equal(bottleId, detail.GetProperty("id").GetInt32());
        Assert.Equal(2, detail.GetProperty("message_count").GetInt32());
        Assert.Equal(throwerName, detail.GetProperty("thrower_name").GetString());
        Assert.Equal(pickerName, detail.GetProperty("picker_name").GetString());
        Assert.EndsWith("+08:00", detail.GetProperty("created_at").GetString());
        Assert.EndsWith("+08:00", detail.GetProperty("picked_at").GetString());

        var messages = detail.GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(3, messages.Length);
        Assert.Equal(new[] { first, hidden, third }, messages.Select(m => m.GetProperty("id").GetInt32()).ToArray());
        Assert.Equal("thrower", messages[0].GetProperty("sender_role").GetString());
        Assert.Equal(throwerName, messages[0].GetProperty("sender_name").GetString());
        Assert.Equal(bottleId, messages[0].GetProperty("bottle_id").GetInt32());
        Assert.Equal(MhopBottleMessageStatus.Visible, messages[0].GetProperty("status").GetInt32());
        Assert.Equal("picker", messages[1].GetProperty("sender_role").GetString());
        Assert.Equal(MhopBottleMessageStatus.Hidden, messages[1].GetProperty("status").GetInt32());

        var (missing, missingBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles/999999", admin);
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Equal("瓶子不存在", missingBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Remove_restore_and_approve_follow_domain_transitions()
    {
        var admin = await AdminTokenAsync();
        const int throwerId = 7003;

        // 海中（未捞起）的疑似瓶子：下架 → 恢复回海中并清空 AI 标记，人工理由保留
        var driftingId = await SeedBottleAsync(throwerId, MhopBottleStatus.Drifting, "疑似违规",
            aiFlag: MhopModerationOutcome.Suspect);
        var (remove, removeBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/" + driftingId + "/remove",
            new { note = "违规内容" }, admin);
        Assert.Equal(HttpStatusCode.OK, remove);
        Assert.True(removeBody.RootElement.GetProperty("ok").GetBoolean());

        var removed = await AdminDetailAsync(admin, driftingId);
        Assert.Equal(MhopBottleStatus.Removed, removed.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("违规内容", removed.RootElement.GetProperty("review_note").GetString());
        Assert.Equal(MhopModerationOutcome.Suspect, removed.RootElement.GetProperty("ai_flag").GetString());

        var (restore, _) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/" + driftingId + "/restore",
            new { note = "误判，恢复" }, admin);
        Assert.Equal(HttpStatusCode.OK, restore);
        var restored = await AdminDetailAsync(admin, driftingId);
        Assert.Equal(MhopBottleStatus.Drifting, restored.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(string.Empty, restored.RootElement.GetProperty("ai_flag").GetString());
        Assert.Equal("误判，恢复", restored.RootElement.GetProperty("review_note").GetString());

        // 待审瓶子：人工放行 → 海中 + approved；再次放行 409
        var pendingId = await SeedBottleAsync(throwerId, MhopBottleStatus.Pending, "等待人工");
        var (approve, _) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/" + pendingId + "/approve",
            new { note = "人工放行" }, admin);
        Assert.Equal(HttpStatusCode.OK, approve);
        var approved = await AdminDetailAsync(admin, pendingId);
        Assert.Equal(MhopBottleStatus.Drifting, approved.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(MhopModerationOutcome.Approved, approved.RootElement.GetProperty("ai_flag").GetString());
        Assert.Equal("人工放行", approved.RootElement.GetProperty("review_note").GetString());

        var (again, againBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/" + pendingId + "/approve",
            new { note = string.Empty }, admin);
        Assert.Equal(HttpStatusCode.Conflict, again);
        Assert.Equal("该瓶子不在待审核状态", againBody.RootElement.GetProperty("detail").GetString());

        var (missing, missingBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/999999/remove",
            new { note = "x" }, admin);
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Equal("瓶子不存在", missingBody.RootElement.GetProperty("detail").GetString());
    }

    [Fact]
    public async Task Message_queue_lists_only_actionable_rows_and_hide_restore_affects_frontend()
    {
        var admin = await AdminTokenAsync();
        var (throwerToken, throwerId, throwerName) = await RegisterAsync("bottle_msg_thrower");
        var (pickerToken, pickerId, _) = await RegisterAsync("bottle_msg_picker");
        var bottleId = await SeedBottleAsync(throwerId, MhopBottleStatus.Picked, "带消息的对话", pickerId: pickerId);
        var visibleId = await SeedMessageAsync(bottleId, pickerId, "正常消息");
        var hiddenId = await SeedMessageAsync(bottleId, throwerId, "被隐藏的消息", hidden: true);

        var (queue, queueBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles/messages", admin);
        Assert.Equal(HttpStatusCode.OK, queue);
        Assert.Equal(1, queueBody.RootElement.GetProperty("total").GetInt32());
        var queued = queueBody.RootElement.GetProperty("items")[0];
        Assert.Equal(hiddenId, queued.GetProperty("id").GetInt32());
        Assert.Equal(MhopBottleMessageStatus.Hidden, queued.GetProperty("status").GetInt32());
        Assert.Equal(throwerId, queued.GetProperty("sender_id").GetInt32());
        Assert.Equal(throwerName, queued.GetProperty("sender_name").GetString());
        Assert.Equal("thrower", queued.GetProperty("sender_role").GetString());

        var (byStatus, byStatusBody) = await JsonHttp.GetJsonAsync(Client, "/mhop/admin/bottles/messages?status=1", admin);
        Assert.Equal(HttpStatusCode.OK, byStatus);
        Assert.Equal(1, byStatusBody.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(visibleId, byStatusBody.RootElement.GetProperty("items")[0].GetProperty("id").GetInt32());

        // 隐藏正常消息 → 前台立即不可见
        var (hide, _) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/messages/" + visibleId + "/hide", new { }, admin);
        Assert.Equal(HttpStatusCode.OK, hide);
        var (_, afterHide) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/" + bottleId, pickerToken);
        Assert.Empty(afterHide.RootElement.GetProperty("messages").EnumerateArray());

        // 恢复 → 前台重新可见并打上 approved
        var (restore, _) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/messages/" + visibleId + "/restore", new { }, admin);
        Assert.Equal(HttpStatusCode.OK, restore);
        var (_, afterRestore) = await JsonHttp.GetJsonAsync(Client, "/mhop/bottles/" + bottleId, throwerToken);
        var shown = Assert.Single(afterRestore.RootElement.GetProperty("messages").EnumerateArray());
        Assert.Equal(visibleId, shown.GetProperty("id").GetInt32());
        Assert.Equal(MhopBottleMessageStatus.Visible, shown.GetProperty("status").GetInt32());

        var (missing, missingBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/messages/999999/hide", new { }, admin);
        Assert.Equal(HttpStatusCode.NotFound, missing);
        Assert.Equal("消息不存在", missingBody.RootElement.GetProperty("detail").GetString());

        // AI 重跑：只入队，不校验存在性，形状固定
        var (rescreen, rescreenBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/" + bottleId + "/rescreen", new { }, admin);
        Assert.Equal(HttpStatusCode.OK, rescreen);
        Assert.True(rescreenBody.RootElement.GetProperty("ok").GetBoolean());
        Assert.True(rescreenBody.RootElement.GetProperty("queued").GetBoolean());

        var (rescreenMsg, rescreenMsgBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/messages/" + hiddenId + "/rescreen", new { }, admin);
        Assert.Equal(HttpStatusCode.OK, rescreenMsg);
        Assert.True(rescreenMsgBody.RootElement.GetProperty("queued").GetBoolean());

        var (rescreenGhost, rescreenGhostBody) = await JsonHttp.PostJsonAsync(Client, "/mhop/admin/bottles/999999/rescreen", new { }, admin);
        Assert.Equal(HttpStatusCode.OK, rescreenGhost);
        Assert.True(rescreenGhostBody.RootElement.GetProperty("queued").GetBoolean());
    }
}
