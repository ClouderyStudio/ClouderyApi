using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>
/// 漂流瓶后台审核：统计、队列、瓶子下架/恢复、消息隐藏/恢复。
/// 独立 bottles 权限码，不并入论坛 review。
/// </summary>
[ApiController]
[Route("mhop/admin/bottles")]
[MhopAdmin]
public class MhopBottleAdminController(
    MhopBottleService bottles,
    MhopDbContext db,
    MhopCurrentUserAccessor current) : MhopControllerBase
{
    [HttpGet("stats")]
    [MhopPerm(MhopAdminPermissions.Bottles)]
    public async Task<IActionResult> Stats()
    {
        var (crisis, suspect, reported, pending) = await bottles.AdminStatsAsync();
        return MhopOk(new AdminBottleStatsOut
        {
            Crisis = crisis,
            Suspect = suspect,
            Reported = reported,
            Pending = pending,
        });
    }

    [HttpGet]
    [MhopPerm(MhopAdminPermissions.Bottles)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        [FromQuery] int? status = null,
        [FromQuery] string? flag = null,
        [FromQuery] bool reported = false)
    {
        page = Math.Max(1, page);
        size = Math.Clamp(size, 1, 100);

        var (total, items) = await bottles.AdminListAsync(page, size, status, flag, reported);

        // 批量取真实用户名（扔瓶人 + 捞瓶人）
        var userIds = items.SelectMany(b => new[] { b.UserId, b.PickerUserId ?? 0 })
            .Where(id => id > 0).Distinct().ToList();
        var names = await LoadNamesAsync(userIds);

        // 批量取每个瓶子的可见消息数
        var bottleIds = items.Select(b => b.Id).ToList();
        var counts = bottleIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.MhopBottleMessages.AsNoTracking()
                .Where(m => bottleIds.Contains(m.BottleId) && m.Status == 1)
                .GroupBy(m => m.BottleId)
                .Select(g => new { BottleId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.BottleId, x => x.Count);

        return MhopOk(new AdminBottleListOut
        {
            Total = total,
            Page = page,
            Size = size,
            Items = items.Select(b =>
                MhopBottleMapper.ToAdminOut(b, names, counts.GetValueOrDefault(b.Id))).ToList(),
        });
    }

    [HttpGet("{id:int}")]
    [MhopPerm(MhopAdminPermissions.Bottles)]
    public async Task<IActionResult> Detail(int id)
    {
        var bottle = await bottles.AdminGetAsync(id);
        if (bottle is null) throw new MhopApiException(404, "瓶子不存在");

        var userIds = new List<int> { bottle.UserId };
        if (bottle.PickerUserId is int pid) userIds.Add(pid);
        var names = await LoadNamesAsync(userIds);

        var detail = MhopBottleMapper.ToAdminOut(bottle, names);
        var messages = bottle.Messages.OrderBy(m => m.Id)
            .Select(m => MhopBottleMapper.ToAdminMessageOut(m, bottle, names)).ToList();

        return MhopOk(new AdminBottleDetailOut
        {
            Id = detail.Id,
            Content = detail.Content,
            Status = detail.Status,
            Crisis = detail.Crisis,
            AiFlag = detail.AiFlag,
            AiReviewNote = detail.AiReviewNote,
            AiReviewedAt = detail.AiReviewedAt,
            ReviewNote = detail.ReviewNote,
            ReportedCount = detail.ReportedCount,
            LastReportedAt = detail.LastReportedAt,
            ReportReason = detail.ReportReason,
            ThrowerId = detail.ThrowerId,
            ThrowerName = detail.ThrowerName,
            PickerId = detail.PickerId,
            PickerName = detail.PickerName,
            MessageCount = messages.Count(m => m.Status == 1),
            EndReason = detail.EndReason,
            CreatedAt = detail.CreatedAt,
            PickedAt = detail.PickedAt,
            LastMessageAt = detail.LastMessageAt,
            Messages = messages,
        });
    }

    [HttpPost("{id:int}/remove")]
    [MhopPerm(MhopAdminPermissions.Bottles)]
    public async Task<IActionResult> Remove(int id, [FromBody] BottleAdminActionIn? body)
    {
        var admin = await current.RequirePermAsync(MhopAdminPermissions.Bottles);
        await bottles.AdminSetBottleStatusAsync(id, MhopBottleStatus.Removed, body?.Note ?? string.Empty, admin.Id);
        return MhopOk(new { ok = true });
    }

    [HttpPost("{id:int}/restore")]
    [MhopPerm(MhopAdminPermissions.Bottles)]
    public async Task<IActionResult> Restore(int id, [FromBody] BottleAdminActionIn? body)
    {
        var admin = await current.RequirePermAsync(MhopAdminPermissions.Bottles);
        // 恢复目标状态由服务按「是否曾被捞起 / 曾结束」推导（未捞→海中，进行中→对话中，已结束→保持）
        await bottles.AdminSetBottleStatusAsync(
            id,
            MhopBottleStatus.Drifting,
            body?.Note ?? string.Empty,
            admin.Id);
        return MhopOk(new { ok = true });
    }

    [HttpPost("messages/{messageId:int}/hide")]
    [MhopPerm(MhopAdminPermissions.Bottles)]
    public async Task<IActionResult> HideMessage(int messageId)
    {
        var admin = await current.RequirePermAsync(MhopAdminPermissions.Bottles);
        await bottles.AdminSetMessageStatusAsync(messageId, 2, admin.Id);
        return MhopOk(new { ok = true });
    }

    [HttpPost("messages/{messageId:int}/restore")]
    [MhopPerm(MhopAdminPermissions.Bottles)]
    public async Task<IActionResult> RestoreMessage(int messageId)
    {
        var admin = await current.RequirePermAsync(MhopAdminPermissions.Bottles);
        await bottles.AdminSetMessageStatusAsync(messageId, 1, admin.Id);
        return MhopOk(new { ok = true });
    }

    private async Task<Dictionary<int, string>> LoadNamesAsync(IEnumerable<int> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        return await db.MhopUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username);
    }
}
