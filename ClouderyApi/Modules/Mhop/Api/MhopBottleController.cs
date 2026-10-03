using ClouderyApi.Modules.Mhop.Api.Contracts;
using ClouderyApi.Modules.Mhop.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using ClouderyApi.Modules.Mhop.Application;
using ClouderyApi.Modules.Mhop.Application.Mapping;
using ClouderyApi.Shared.Filters;

namespace ClouderyApi.Modules.Mhop.Api;

/// <summary>
/// 漂流瓶（前台）：投瓶、捞瓶、匿名持续对话、结束、举报。
/// 全部接口需登录；会话访问严格限定双方，越权一律 404（不泄露存在性）。
/// </summary>
[ApiController]
[Route("mhop/bottles")]
public class MhopBottleController(
    BottleAppService bottles,
    MhopCurrentUserAccessor current) : MhopControllerBase
{
    private const string Hotline = "12356";

    /// <summary>扔出一个瓶子。每次投瓶都会触发一次 AI 内容审核，按 IP 限流保护模型开销。</summary>
    [HttpPost]
    [IpRateLimit(MaxRequests = 20, WindowSeconds = 300, UseMhopErrorShape = true)]
    public async Task<IActionResult> Throw([FromBody] ThrowIn body)
    {
        var user = await current.RequireAsync();
        var bottle = await bottles.ThrowAsync(user, body.Content);
        return MhopOk(new
        {
            id = bottle.Id,
            status = bottle.Status,
            crisis = bottle.Crisis,
            hotline = bottle.Crisis ? Hotline : null,
        });
    }

    /// <summary>随机捞起一个他人的漂流瓶；海里暂无可捞瓶时返回 409 + 友好文案。</summary>
    [HttpPost("pick")]
    public async Task<IActionResult> Pick()
    {
        var user = await current.RequireAsync();
        var picked = await bottles.PickAsync(user);
        if (picked is null)
            return MhopStatus(409, new { detail = "海里暂时没有漂着的瓶子，先扔一个，稍后再来捞捞看吧" });

        var detail = await bottles.GetDetailAsync(picked.Id, user.Id, markRead: false);
        if (detail is not { } d) return NotFound(new { detail = "会话不存在" });
        return MhopOk(MhopBottleMapper.ToDetailOut(d.Bottle, user.Id, d.Unread));
    }

    /// <summary>我的瓶子列表 + 今日剩余次数 + 海计数。</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine()
    {
        var user = await current.RequireAsync();
        var (items, thrown, picked, sea) = await bottles.GetMineBundleAsync(user.Id);
        return MhopOk(new BottleMineOut
        {
            Items = items.Select(b => MhopBottleMapper.ToSummaryOut(b, user.Id)).ToList(),
            ThrownToday = thrown,
            PickedToday = picked,
            ThrowLimit = BottleAppService.ThrowDailyLimit,
            PickLimit = BottleAppService.PickDailyLimit,
            SeaCount = sea,
        });
    }

    [HttpGet("sea/count")]
    public async Task<IActionResult> SeaCount()
    {
        var user = await current.RequireAsync();
        // 海计数排除本人的瓶子，与随机捞瓶口径一致，避免「显示有瓶却捞不到」
        return MhopOk(new { count = await bottles.SeaCountAsync(user.Id) });
    }

    /// <summary>会话详情（含消息）。after_id 用于轮询增量；进入页面时回写已读。</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(
        int id,
        [FromQuery(Name = "after_id")] int afterId = 0,
        [FromQuery(Name = "mark_read")] bool markRead = true)
    {
        var user = await current.RequireAsync();
        var data = await bottles.GetDetailAsync(id, user.Id, markRead);
        if (data is null) return NotFound(new { detail = "会话不存在" });
        return MhopOk(MhopBottleMapper.ToDetailOut(data.Value.Bottle, user.Id, data.Value.Unread, afterId));
    }

    /// <summary>发送一条消息。每次都会触发一次 AI 内容审核，按 IP 限流。</summary>
    [HttpPost("{id:int}/messages")]
    [IpRateLimit(MaxRequests = 30, WindowSeconds = 300, UseMhopErrorShape = true)]
    public async Task<IActionResult> Send(int id, [FromBody] SendIn body)
    {
        var user = await current.RequireAsync();
        var message = await bottles.SendMessageAsync(user, id, body.Content);
        return MhopOk(MhopBottleMapper.ToMessageOut(message, user.Id));
    }

    /// <summary>主动结束对话（幂等）。</summary>
    [HttpPost("{id:int}/end")]
    public async Task<IActionResult> End(int id)
    {
        var user = await current.RequireAsync();
        await bottles.EndAsync(user.Id, id);
        return MhopOk(new { success = true });
    }

    /// <summary>举报瓶子。</summary>
    [HttpPost("{id:int}/report")]
    public async Task<IActionResult> Report(int id, [FromBody] ReportIn body)
    {
        var user = await current.RequireAsync();
        await bottles.ReportAsync(user.Id, id, body.Reason);
        return MhopOk(new { success = true });
    }
}
