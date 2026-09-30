using System.Text.Json;
using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 漂流瓶领域服务：投瓶/捞瓶/匿名对话的全部业务规则。
/// 安全要点：捞瓶用条件更新抢占防并发双捞；频控；关键词同步拦截 + AI 异步初筛可降级；
/// 7 天无消息惰性结束（另有后台定时任务兜底）；前台不暴露对方真实身份。
/// </summary>
public sealed class MhopBottleService
{
    public const int ThrowDailyLimit = 3;
    public const int PickDailyLimit = 10;
    public const int MessagePerMinuteLimit = 20;
    public const int BottleMaxLength = 500;
    public const int MessageMaxLength = 1000;
    public const int ReportReasonMaxLength = 200;
    public static readonly TimeSpan ConversationTimeout = TimeSpan.FromDays(7);

    private readonly MhopDbContext _db;
    private readonly MhopAiService _ai;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MhopBottleService> _logger;

    public MhopBottleService(
        MhopDbContext db,
        MhopAiService ai,
        IServiceScopeFactory scopes,
        ILogger<MhopBottleService> logger)
    {
        _db = db;
        _ai = ai;
        _scopes = scopes;
        _logger = logger;
    }

    // ---------------- 投瓶 ----------------

    public async Task<MhopBottle> ThrowAsync(MhopUser user, string rawContent)
    {
        var content = (rawContent ?? string.Empty).Trim();
        if (content.Length is 0 or > BottleMaxLength)
            throw new MhopApiException(422, $"瓶子内容需为 1-{BottleMaxLength} 字");

        if (MhopModeration.HitSensitive(content) is { Count: > 0 })
            throw new MhopApiException(422, "内容可能包含不当或违规信息，请修改后再扔出");

        var today = DateTime.UtcNow.Date;
        var thrownToday = await _db.MhopBottles
            .CountAsync(b => b.UserId == user.Id && b.CreatedAt >= today);
        if (thrownToday >= ThrowDailyLimit)
            throw new MhopApiException(429, $"今天已经扔了 {ThrowDailyLimit} 个瓶子，明天再来吧");

        var now = DateTime.UtcNow;
        var crisis = MhopModeration.DetectCrisis(content);
        var bottle = new MhopBottle
        {
            UserId = user.Id,
            Content = content,
            Status = MhopBottleStatus.Drifting,
            Crisis = crisis,
            LastMessageAt = now,
            CreatedAt = now,
            ThrowerLastReadAt = now, // 瓶身是自己写的，无未读
        };
        _db.MhopBottles.Add(bottle);
        await _db.SaveChangesAsync();
        _logger.LogInformation("漂流瓶 {BottleId} 由用户 {UserId} 扔出，危机标记 {Crisis}", bottle.Id, user.Id, crisis);

        QueueAiScreen(bottle.Id, null);
        return bottle;
    }

    // ---------------- 捞瓶 ----------------

    /// <summary>随机捞起一个他人的漂流瓶（独占）。海里没有可捞瓶时返回 null。</summary>
    public async Task<MhopBottle?> PickAsync(MhopUser user)
    {
        var today = DateTime.UtcNow.Date;
        var pickedToday = await _db.MhopBottles
            .CountAsync(b => b.PickerUserId == user.Id && b.PickedAt >= today);
        if (pickedToday >= PickDailyLimit)
            throw new MhopApiException(429, $"今天已经捞了 {PickDailyLimit} 个瓶子，明天再来吧");

        var now = DateTime.UtcNow;
        // 条件更新抢占：子查询 RAND() 随机选一个他人的漂流瓶，JOIN 后只更新仍是 Drifting 的行。
        // 高并发下两个请求不会同时改到同一行，行锁保证一瓶仅有一个捞瓶人。
        var affected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE mhop_bottles AS b
JOIN (
    SELECT Id FROM mhop_bottles
    WHERE Status = 1 AND UserId <> {user.Id}
    ORDER BY RAND() LIMIT 1
) AS c ON b.Id = c.Id
SET b.Status = 2, b.PickerUserId = {user.Id}, b.PickedAt = {now}
WHERE b.Status = 1");
        if (affected == 0) return null;

        var bottle = await _db.MhopBottles
            .Where(b => b.PickerUserId == user.Id)
            .OrderByDescending(b => b.Id)
            .FirstAsync();
        // 捞起即视为已读瓶身
        bottle.PickerLastReadAt = now;
        await _db.SaveChangesAsync();
        _logger.LogInformation("漂流瓶 {BottleId} 被用户 {UserId} 捞起", bottle.Id, user.Id);
        return bottle;
    }

    public Task<int> SeaCountAsync()
        => _db.MhopBottles.CountAsync(b => b.Status == MhopBottleStatus.Drifting);

    /// <summary>「我的瓶子」页一次性聚合：会话列表 + 今日次数 + 海中数量。</summary>
    public async Task<(List<MhopBottle> Items, int ThrownToday, int PickedToday, int SeaCount)>
        GetMineBundleAsync(int userId)
    {
        var items = await ListMineAsync(userId);
        var today = DateTime.UtcNow.Date;
        var thrown = await _db.MhopBottles.CountAsync(b => b.UserId == userId && b.CreatedAt >= today);
        var picked = await _db.MhopBottles.CountAsync(b => b.PickerUserId == userId && b.PickedAt >= today);
        var sea = await SeaCountAsync();
        return (items, thrown, picked, sea);
    }

    // ---------------- 会话读取 ----------------

    /// <summary>惰性关闭超时会话；返回受影响条数。定时任务之外，每次访问也会触发。</summary>
    public async Task<int> ApplyTimeoutAsync()
    {
        var cutoff = DateTime.UtcNow - ConversationTimeout;
        return await _db.MhopBottles
            .Where(b => b.Status == MhopBottleStatus.Picked && b.LastMessageAt < cutoff)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, MhopBottleStatus.Ended)
                .SetProperty(b => b.EndReason, MhopBottleEndReason.Timeout));
    }

    public async Task<List<MhopBottle>> ListMineAsync(int userId)
    {
        await ApplyTimeoutAsync();
        return await _db.MhopBottles
            .Include(b => b.Messages)
            .Where(b => b.UserId == userId || b.PickerUserId == userId)
            .OrderByDescending(b => b.LastMessageAt)
            .Take(100)
            .ToListAsync();
    }

    /// <summary>取会话详情（仅会话双方）。返回 null 表示不存在或无权访问（控制器转 404）。</summary>
    public async Task<(MhopBottle Bottle, int Unread)?> GetDetailAsync(int bottleId, int userId, bool markRead)
    {
        await ApplyTimeoutAsync();
        var bottle = await _db.MhopBottles
            .Include(b => b.Messages)
            .FirstOrDefaultAsync(b => b.Id == bottleId);
        if (bottle is null || !IsParty(bottle, userId)) return null;

        var unread = CountUnread(bottle, userId);
        if (markRead)
        {
            var now = DateTime.UtcNow;
            if (bottle.UserId == userId) bottle.ThrowerLastReadAt = now;
            else bottle.PickerLastReadAt = now;
            await _db.SaveChangesAsync();
        }
        return (bottle, unread);
    }

    // ---------------- 消息 ----------------

    public async Task<MhopBottleMessage> SendMessageAsync(MhopUser user, int bottleId, string rawContent)
    {
        var content = (rawContent ?? string.Empty).Trim();
        if (content.Length is 0 or > MessageMaxLength)
            throw new MhopApiException(422, $"消息需为 1-{MessageMaxLength} 字");

        await ApplyTimeoutAsync();
        var bottle = await _db.MhopBottles.FirstOrDefaultAsync(b => b.Id == bottleId);
        if (bottle is null || !IsParty(bottle, user.Id))
            throw new MhopApiException(404, "会话不存在");
        if (bottle.Status != MhopBottleStatus.Picked)
            throw new MhopApiException(409, bottle.Status == MhopBottleStatus.Removed ? "该内容因违规已被下架" : "对话已经结束");

        if (MhopModeration.HitSensitive(content) is { Count: > 0 })
            throw new MhopApiException(422, "消息可能包含不当或违规信息，请修改后再发送");

        var since = DateTime.UtcNow.AddMinutes(-1);
        var recentCount = await _db.MhopBottleMessages
            .CountAsync(m => m.SenderUserId == user.Id && m.CreatedAt >= since);
        if (recentCount >= MessagePerMinuteLimit)
            throw new MhopApiException(429, "发送太频繁了，稍后再试");

        var now = DateTime.UtcNow;
        var message = new MhopBottleMessage
        {
            BottleId = bottle.Id,
            SenderUserId = user.Id,
            Content = content,
            Crisis = MhopModeration.DetectCrisis(content),
            CreatedAt = now,
        };
        _db.MhopBottleMessages.Add(message);
        bottle.LastMessageAt = now;
        await _db.SaveChangesAsync();

        QueueAiScreen(bottle.Id, message.Id);
        return message;
    }

    // ---------------- 结束 / 举报 ----------------

    /// <summary>主动结束会话（仅双方，幂等）。返回是否本次执行了结束。</summary>
    public async Task<bool> EndAsync(int userId, int bottleId)
    {
        await ApplyTimeoutAsync();
        var bottle = await _db.MhopBottles.FirstOrDefaultAsync(b => b.Id == bottleId);
        if (bottle is null || !IsParty(bottle, userId))
            throw new MhopApiException(404, "会话不存在");
        if (bottle.Status != MhopBottleStatus.Picked) return false;

        bottle.Status = MhopBottleStatus.Ended;
        bottle.EndReason = MhopBottleEndReason.Manual;
        bottle.EndedByUserId = userId;
        await _db.SaveChangesAsync();
        _logger.LogInformation("漂流瓶 {BottleId} 被用户 {UserId} 主动结束", bottleId, userId);
        return true;
    }

    public async Task ReportAsync(int userId, int bottleId, string rawReason)
    {
        var reason = (rawReason ?? string.Empty).Trim();
        if (reason.Length > ReportReasonMaxLength)
            throw new MhopApiException(422, $"举报理由不超过 {ReportReasonMaxLength} 字");

        var bottle = await _db.MhopBottles.FirstOrDefaultAsync(b => b.Id == bottleId);
        if (bottle is null || !IsParty(bottle, userId))
            throw new MhopApiException(404, "会话不存在");

        var reporterIds = ParseIdList(bottle.ReportedBy);
        if (reporterIds.Add(userId))
        {
            bottle.ReportedBy = JsonSerializer.Serialize(reporterIds);
            bottle.ReportedCount = reporterIds.Count;
            bottle.LastReportedAt = DateTime.UtcNow;
            bottle.ReportReason = reason;
            await _db.SaveChangesAsync();
            _logger.LogWarning("漂流瓶 {BottleId} 被用户 {UserId} 举报：{Reason}", bottleId, userId, reason);
        }
    }

    // ---------------- 后台审核 ----------------

    public async Task<(int Total, List<MhopBottle> Items)> AdminListAsync(
        int page, int size, int? status, string? flag, bool reportedOnly)
    {
        var query = _db.MhopBottles.AsNoTracking().AsQueryable();
        if (status is int s) query = query.Where(b => b.Status == s);
        if (!string.IsNullOrWhiteSpace(flag)) query = query.Where(b => b.AiFlag == flag);
        if (reportedOnly) query = query.Where(b => b.ReportedCount > 0 && b.Status != MhopBottleStatus.Removed);

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(b => b.ReportedCount)
            .ThenByDescending(b => b.Crisis)
            .ThenByDescending(b => b.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync();
        return (total, items);
    }

    public Task<MhopBottle?> AdminGetAsync(int bottleId)
        => _db.MhopBottles.Include(b => b.Messages)
            .AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == bottleId);

    public Task<int> AdminMessageCountAsync()
        => _db.MhopBottleMessages.CountAsync();

    public async Task<(int Crisis, int Suspect, int Reported)> AdminStatsAsync()
    {
        var crisis = await _db.MhopBottles.CountAsync(b =>
            b.Crisis && (b.Status == MhopBottleStatus.Drifting || b.Status == MhopBottleStatus.Picked));
        var suspectBottles = await _db.MhopBottles.CountAsync(b =>
            b.AiFlag == "suspect" && b.Status != MhopBottleStatus.Removed);
        var suspectMessages = await _db.MhopBottleMessages.CountAsync(m =>
            m.AiFlag == "suspect" && m.Status == 1);
        var reported = await _db.MhopBottles.CountAsync(b =>
            b.ReportedCount > 0 && b.Status != MhopBottleStatus.Removed);
        return (crisis, suspectBottles + suspectMessages, reported);
    }

    public async Task AdminSetBottleStatusAsync(int bottleId, int status, string note, int adminUserId)
    {
        var bottle = await _db.MhopBottles.FirstOrDefaultAsync(b => b.Id == bottleId)
            ?? throw new MhopApiException(404, "瓶子不存在");

        if (status == MhopBottleStatus.Removed)
        {
            bottle.Status = MhopBottleStatus.Removed;
        }
        else if (status == MhopBottleStatus.Drifting || status == MhopBottleStatus.Picked)
        {
            // 恢复：未被捞过的回到海中；已建立过对话的回到对话中（若曾结束则保持结束态）
            bottle.Status = bottle.PickerUserId is null
                ? MhopBottleStatus.Drifting
                : bottle.EndReason is null ? MhopBottleStatus.Picked : bottle.Status;
            if (bottle.Status != MhopBottleStatus.Removed) bottle.AiFlag = string.Empty;
        }
        else
        {
            throw new MhopApiException(422, "不支持的处置状态");
        }

        if (!string.IsNullOrWhiteSpace(note)) bottle.ReviewNote = note.Trim();
        await _db.SaveChangesAsync();
        _logger.LogWarning("管理员 {AdminId} 处置漂流瓶 {BottleId} → 状态 {Status}", adminUserId, bottleId, bottle.Status);
    }

    public async Task AdminSetMessageStatusAsync(int messageId, int status, int adminUserId)
    {
        var message = await _db.MhopBottleMessages.FirstOrDefaultAsync(m => m.Id == messageId)
            ?? throw new MhopApiException(404, "消息不存在");
        if (status is not (1 or 2)) throw new MhopApiException(422, "不支持的消息状态");
        message.Status = status;
        if (status == 1 && message.AiFlag == "violation") message.AiFlag = string.Empty;
        await _db.SaveChangesAsync();
        _logger.LogWarning("管理员 {AdminId} 处置漂流瓶消息 {MessageId} → 状态 {Status}", adminUserId, messageId, status);
    }

    // ---------------- 辅助 ----------------

    public static bool IsParty(MhopBottle b, int userId)
        => b.UserId == userId || b.PickerUserId == userId;

    public static string RoleOf(MhopBottle b, int userId)
        => b.UserId == userId ? "thrower" : "picker";

    public static int CountUnread(MhopBottle b, int userId)
    {
        var readAt = b.UserId == userId ? b.ThrowerLastReadAt : b.PickerLastReadAt;
        return b.Messages.Count(m =>
            m.Status == 1 && m.SenderUserId != userId &&
            (readAt is null || m.CreatedAt > readAt));
    }

    private static HashSet<int> ParseIdList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json ?? "[]")?.ToHashSet() ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// AI 异步初筛：独立 DI 作用域执行，结果回写 AiFlag 供人工队列处置；
    /// 任何异常都静默降级（关键词检测已同步执行过），绝不阻塞或影响主流程。
    /// </summary>
    private void QueueAiScreen(int bottleId, int? messageId)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                string text;
                using (var readScope = _scopes.CreateScope())
                {
                    var db = readScope.ServiceProvider.GetRequiredService<MhopDbContext>();
                    if (messageId is int mid)
                    {
                        var msg = await db.MhopBottleMessages.AsNoTracking()
                            .FirstOrDefaultAsync(m => m.Id == mid);
                        if (msg is null) return;
                        text = msg.Content;
                    }
                    else
                    {
                        var bottle = await db.MhopBottles.AsNoTracking()
                            .FirstOrDefaultAsync(b => b.Id == bottleId);
                        if (bottle is null) return;
                        text = bottle.Content;
                    }
                }

                var flag = await _ai.ModerateTextAsync(text);
                if (string.IsNullOrEmpty(flag)) return;

                using var writeScope = _scopes.CreateScope();
                var writeDb = writeScope.ServiceProvider.GetRequiredService<MhopDbContext>();
                if (messageId is int msgId2)
                {
                    await writeDb.MhopBottleMessages
                        .Where(m => m.Id == msgId2 && m.AiFlag == string.Empty)
                        .ExecuteUpdateAsync(s => s.SetProperty(m => m.AiFlag, flag));
                }
                else
                {
                    await writeDb.MhopBottles
                        .Where(b => b.Id == bottleId && b.AiFlag == string.Empty)
                        .ExecuteUpdateAsync(s => s.SetProperty(b => b.AiFlag, flag));
                }
                _logger.LogInformation(
                    "漂流瓶 AI 初筛完成：bottle={BottleId} message={MessageId} flag={Flag}",
                    bottleId, messageId, flag);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "漂流瓶 AI 初筛失败，已降级为仅关键词检测");
            }
        });
    }
}
