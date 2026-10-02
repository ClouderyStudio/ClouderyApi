using System.Collections.Concurrent;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Modules.Mhop.Domain;
using Microsoft.EntityFrameworkCore;
using ClouderyApi.Modules.Mhop.Infrastructure;

using ClouderyApi.Shared.Exceptions;

namespace ClouderyApi.Modules.Mhop.Application;

/// <summary>
/// 漂流瓶领域服务：投瓶/捞瓶/匿名对话的全部业务规则。
/// 安全要点：捞瓶用条件更新抢占防并发双捞；频控；关键词同步拦截 + AI 异步初筛可降级；
/// 7 天无消息惰性结束（另有后台定时任务兜底）；前台不暴露对方真实身份。
/// </summary>
public sealed class BottleAppService
{
    public const int ThrowDailyLimit = 3;
    public const int PickDailyLimit = 10;
    public const int MessagePerMinuteLimit = 20;
    public const int BottleMaxLength = 500;
    public const int MessageMaxLength = 1000;
    public const int ReportReasonMaxLength = 200;
    public static readonly TimeSpan ConversationTimeout = TimeSpan.FromDays(7);

    /// <summary>后台筛选特殊值：只看还没有任何 AI 标记的内容。</summary>
    public const string AdminFilterPending = "pending";

    private readonly MhopDbContext _db;
    private readonly ILogger<BottleAppService> _logger;

    // 频控防并发闸门：单实例部署下把同一用户的投瓶/捞瓶/发消息、同一瓶子的举报计数串行化，
    // 杜绝「先 count 后写」被同批并发请求打穿。多实例水平扩展时需改为分布式锁或数据库原子计数。
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> UserGates = new();
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> BottleGates = new();

    private static SemaphoreSlim UserGate(int userId)
        => UserGates.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

    private static SemaphoreSlim BottleGate(int bottleId)
        => BottleGates.GetOrAdd(bottleId, _ => new SemaphoreSlim(1, 1));

    public BottleAppService(
        MhopDbContext db,
        ILogger<BottleAppService> logger)
    {
        _db = db;
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

        var gate = UserGate(user.Id);
        await gate.WaitAsync();
        try
        {
            var today = DateTime.UtcNow.Date;
            var thrownToday = await _db.MhopBottles
                .CountAsync(b => b.UserId == user.Id && b.CreatedAt >= today);
            if (thrownToday >= ThrowDailyLimit)
                throw new MhopApiException(429, $"今天已经扔了 {ThrowDailyLimit} 个瓶子，明天再来吧");

            var now = DateTime.UtcNow;
            // 先送 AI 自动审核：Throw 登记 BottleThrown，SaveChanges 提交后由领域事件排队初筛；
            // 通过即自动放入海中，未通过则停留待审核并转人工。
            var bottle = MhopBottle.Throw(user.Id, content, MhopModeration.DetectCrisis(content), now);
            _db.MhopBottles.Add(bottle);
            await _db.SaveChangesAsync();
            _logger.LogInformation("漂流瓶 {BottleId} 由用户 {UserId} 扔出，危机标记 {Crisis}", bottle.Id, user.Id, bottle.Crisis);

            return bottle;
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------------- 捞瓶 ----------------

    /// <summary>随机捞起一个他人的漂流瓶（独占）。海里没有可捞瓶时返回 null。</summary>
    public async Task<MhopBottle?> PickAsync(MhopUser user)
    {
        var gate = UserGate(user.Id);
        await gate.WaitAsync();
        try
        {
            var today = DateTime.UtcNow.Date;
            var pickedToday = await _db.MhopBottles
                .CountAsync(b => b.PickerUserId == user.Id && b.PickedAt >= today);
            if (pickedToday >= PickDailyLimit)
                throw new MhopApiException(429, $"今天已经捞了 {PickDailyLimit} 个瓶子，明天再来吧");

            var now = DateTime.UtcNow;
            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // 先随机选一个候选，再按 Id + Status=1 条件更新抢占。
                // 与其他捞瓶请求撞同一瓶时，InnoDB 行锁串行化两条 UPDATE，
                // 后到者 WHERE Status=1 匹配 0 行 → 换候选重试，保证一瓶不会被两人捞走。
                // 不能按「我捞过的最大 Id」反查：那可能取到历史会话而非本次捞到的瓶子。
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    // 标量 SqlQuery<int> 的结果列必须命名为 Value：EF 执行时会包一层
                    // SELECT s.`Value` FROM (<原 SQL>) AS s，否则报 Unknown column 's.Value'。
                    var candidateId = await _db.Database
                        .SqlQuery<int>($"""
                            SELECT Id AS `Value` FROM mhop_bottles
                            WHERE Status = {MhopBottleStatus.Drifting} AND UserId <> {user.Id}
                            ORDER BY RAND() LIMIT 1
                            """)
                        .FirstOrDefaultAsync();
                    if (candidateId == 0) break; // 海里没有可捞瓶

                    var affected = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                        UPDATE mhop_bottles
                        SET Status = {MhopBottleStatus.Picked}, PickerUserId = {user.Id}, PickedAt = {now}
                        WHERE Id = {candidateId} AND Status = {MhopBottleStatus.Drifting}
                        """);
                    if (affected != 1) continue; // 被别人抢先，换一个

                    var bottle = await _db.MhopBottles.FirstAsync(b => b.Id == candidateId);
                    bottle.Pick(user.Id, now);
                    await _db.SaveChangesAsync();
                    await tx.CommitAsync();
                    _logger.LogInformation("漂流瓶 {BottleId} 被用户 {UserId} 捞起", bottle.Id, user.Id);
                    return bottle;
                }

                await tx.RollbackAsync();
                return null;
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }
        finally
        {
            gate.Release();
        }
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
        if (bottle is null || !bottle.IsParty(userId)) return null;

        var unread = bottle.UnreadCountFor(userId);
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
        if (bottle is null || !bottle.IsParty(user.Id))
            throw new MhopApiException(404, "会话不存在");
        if (bottle.Status != MhopBottleStatus.Picked)
            throw new MhopApiException(409, bottle.Status == MhopBottleStatus.Removed ? "该内容因违规已被下架" : "对话已经结束");

        if (MhopModeration.HitSensitive(content) is { Count: > 0 })
            throw new MhopApiException(422, "消息可能包含不当或违规信息，请修改后再发送");

        var gate = UserGate(user.Id);
        await gate.WaitAsync();
        try
        {
            var since = DateTime.UtcNow.AddMinutes(-1);
            var recentCount = await _db.MhopBottleMessages
                .CountAsync(m => m.SenderUserId == user.Id && m.CreatedAt >= since);
            if (recentCount >= MessagePerMinuteLimit)
                throw new MhopApiException(429, "发送太频繁了，稍后再试");

            var now = DateTime.UtcNow;
            // Create 登记 BottleMessageSent，SaveChanges 提交后由领域事件排队送 AI 初筛
            var message = MhopBottleMessage.Create(bottle.Id, user.Id, content, MhopModeration.DetectCrisis(content), now);
            _db.MhopBottleMessages.Add(message);
            bottle.TouchLastMessage(now);
            await _db.SaveChangesAsync();

            return message;
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------------- 结束 / 举报 ----------------

    /// <summary>主动结束会话（仅双方，幂等）。返回是否本次执行了结束。</summary>
    public async Task<bool> EndAsync(int userId, int bottleId)
    {
        await ApplyTimeoutAsync();
        var bottle = await _db.MhopBottles.FirstOrDefaultAsync(b => b.Id == bottleId);
        if (bottle is null || !bottle.IsParty(userId))
            throw new MhopApiException(404, "会话不存在");
        if (bottle.Status != MhopBottleStatus.Picked) return false;

        bottle.End(userId);
        await _db.SaveChangesAsync();
        _logger.LogInformation("漂流瓶 {BottleId} 被用户 {UserId} 主动结束", bottleId, userId);
        return true;
    }

    public async Task ReportAsync(int userId, int bottleId, string rawReason)
    {
        var reason = (rawReason ?? string.Empty).Trim();
        if (reason.Length > ReportReasonMaxLength)
            throw new MhopApiException(422, $"举报理由不超过 {ReportReasonMaxLength} 字");

        var gate = BottleGate(bottleId);
        await gate.WaitAsync();
        try
        {
            var bottle = await _db.MhopBottles.FirstOrDefaultAsync(b => b.Id == bottleId);
            if (bottle is null || !bottle.IsParty(userId))
                throw new MhopApiException(404, "会话不存在");

            if (bottle.TryReport(userId, reason, DateTime.UtcNow))
            {
                await _db.SaveChangesAsync();
                _logger.LogWarning("漂流瓶 {BottleId} 被用户 {UserId} 举报：{Reason}", bottleId, userId, reason);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    // ---------------- 后台审核 ----------------

    public async Task<(int Total, List<MhopBottle> Items)> AdminListAsync(
        int page, int size, int? status, string? flag, bool reportedOnly)
    {
        var query = _db.MhopBottles.AsNoTracking().AsQueryable();
        if (status is int s) query = query.Where(b => b.Status == s);
        if (!string.IsNullOrWhiteSpace(flag))
        {
            var value = flag.Trim();
            // pending：还在等 AI / 人工给结论（没有任何标记且未公开）
            query = value == AdminFilterPending
                ? query.Where(b => b.AiFlag == string.Empty && b.Status == MhopBottleStatus.Pending)
                : query.Where(b => b.AiFlag == value);
        }
        if (reportedOnly) query = query.Where(b => b.ReportedCount > 0 && b.Status != MhopBottleStatus.Removed);

        var total = await query.CountAsync();
        // 审核队列排序：待审 → 被举报 → 危机 → 有 AI 标记 → 最新
        var items = await query
            .OrderByDescending(b => b.Status == MhopBottleStatus.Pending)
            .ThenByDescending(b => b.ReportedCount)
            .ThenByDescending(b => b.Crisis)
            .ThenByDescending(b => b.AiFlag != string.Empty)
            .ThenByDescending(b => b.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync();
        return (total, items);
    }

    /// <summary>消息审核队列：默认只列出「需要处置」的消息（被隐藏的，或带 AI 标记的）。</summary>
    public async Task<(int Total, List<MhopBottleMessage> Items)> AdminListMessagesAsync(
        int page, int size, string? flag, int? status)
    {
        var query = _db.MhopBottleMessages.AsNoTracking().Include(m => m.Bottle).AsQueryable();
        if (!string.IsNullOrWhiteSpace(flag))
        {
            var value = flag.Trim();
            query = value == AdminFilterPending
                ? query.Where(m => m.AiFlag == string.Empty)
                : query.Where(m => m.AiFlag == value);
        }
        if (status is int s) query = query.Where(m => m.Status == s);
        if (string.IsNullOrWhiteSpace(flag) && status is null)
        {
            query = query.Where(m =>
                m.Status != MhopBottleMessageStatus.Visible
                || m.AiFlag == MhopModerationOutcome.Suspect
                || m.AiFlag == MhopModerationOutcome.Violation
                || m.AiFlag == MhopModerationOutcome.Unavailable);
        }

        var total = await query.CountAsync();
        // 先看「已隐藏」和「疑似/违规」，再看「未定论」，最后按时间倒序
        var items = await query
            .OrderByDescending(m => m.Status != MhopBottleMessageStatus.Visible)
            .ThenByDescending(m => m.AiFlag == MhopModerationOutcome.Violation || m.AiFlag == MhopModerationOutcome.Suspect)
            .ThenByDescending(m => m.Id)
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

    public async Task<MhopBottleAdminStats> AdminStatsAsync()
    {
        var crisis = await _db.MhopBottles.CountAsync(b =>
            b.Crisis && (b.Status == MhopBottleStatus.Drifting || b.Status == MhopBottleStatus.Picked));
        var suspectBottles = await _db.MhopBottles.CountAsync(b =>
            (b.AiFlag == MhopModerationOutcome.Suspect || b.AiFlag == MhopModerationOutcome.Violation)
            && b.Status != MhopBottleStatus.Removed);
        var flaggedMessages = await _db.MhopBottleMessages.CountAsync(m =>
            (m.AiFlag == MhopModerationOutcome.Suspect || m.AiFlag == MhopModerationOutcome.Violation)
            && m.Status == MhopBottleMessageStatus.Visible);
        var reported = await _db.MhopBottles.CountAsync(b =>
            b.ReportedCount > 0 && b.Status != MhopBottleStatus.Removed);
        // 待审核：AI 未通过 / 尚未完成审核，等待人工处置
        var pending = await _db.MhopBottles.CountAsync(b => b.Status == MhopBottleStatus.Pending);
        // AI 没给结论（模型不可用或拒答），需要人工兜底
        var unavailable = await _db.MhopBottles.CountAsync(b =>
            b.AiFlag == MhopModerationOutcome.Unavailable && b.Status == MhopBottleStatus.Pending);
        var hiddenMessages = await _db.MhopBottleMessages.CountAsync(m => m.Status == MhopBottleMessageStatus.Hidden);
        return new MhopBottleAdminStats(
            Crisis: crisis,
            Reported: reported,
            Pending: pending,
            Suspect: suspectBottles,
            AiUnavailable: unavailable,
            FlaggedMessages: flaggedMessages,
            HiddenMessages: hiddenMessages);
    }

    public async Task AdminSetBottleStatusAsync(int bottleId, int status, string note, int adminUserId)
    {
        var bottle = await _db.MhopBottles.FirstOrDefaultAsync(b => b.Id == bottleId)
            ?? throw new MhopApiException(404, "瓶子不存在");

        if (status == MhopBottleStatus.Removed)
        {
            bottle.MarkRemoved();
        }
        else if (status is MhopBottleStatus.Drifting or MhopBottleStatus.Picked)
        {
            // 恢复：未被捞过的回到海中；已建立过对话的回到对话中（若曾结束则保持结束态）
            bottle.Restore();
        }
        else
        {
            throw new MhopApiException(422, "不支持的处置状态");
        }

        bottle.SetReviewNote(note);
        await _db.SaveChangesAsync();
        _logger.LogWarning("管理员 {AdminId} 处置漂流瓶 {BottleId} → 状态 {Status}", adminUserId, bottleId, bottle.Status);
    }

    public async Task AdminSetMessageStatusAsync(int messageId, int status, int adminUserId)
    {
        var message = await _db.MhopBottleMessages.FirstOrDefaultAsync(m => m.Id == messageId)
            ?? throw new MhopApiException(404, "消息不存在");
        if (status is not (MhopBottleMessageStatus.Visible or MhopBottleMessageStatus.Hidden))
            throw new MhopApiException(422, "不支持的消息状态");

        if (status == MhopBottleMessageStatus.Visible)
        {
            // 人工放行：打上 approved，AI 重跑审核不会再自动隐藏这条消息
            message.Show(DateTime.UtcNow);
        }
        else
        {
            message.Hide();
        }
        await _db.SaveChangesAsync();
        _logger.LogWarning("管理员 {AdminId} 处置漂流瓶消息 {MessageId} → 状态 {Status}", adminUserId, messageId, status);
    }

    /// <summary>人工放行待审核的瓶子：直接放入海中，并留下人工结论，避免 AI 重跑翻案。</summary>
    public async Task AdminApproveBottleAsync(int bottleId, string note, int adminUserId)
    {
        var bottle = await _db.MhopBottles.FirstOrDefaultAsync(b => b.Id == bottleId)
            ?? throw new MhopApiException(404, "瓶子不存在");
        if (bottle.Status != MhopBottleStatus.Pending)
            throw new MhopApiException(409, "该瓶子不在待审核状态");

        bottle.Approve(DateTime.UtcNow);
        bottle.SetReviewNote(note);
        await _db.SaveChangesAsync();
        _logger.LogWarning("管理员 {AdminId} 人工放行漂流瓶 {BottleId}", adminUserId, bottleId);
    }

    /// <summary>批量统计瓶子下的可见消息数（后台列表用；控制器不直连 DbContext）。</summary>
    public async Task<Dictionary<int, int>> CountVisibleMessagesAsync(
        IReadOnlyCollection<int> bottleIds, CancellationToken cancellationToken = default)
    {
        var ids = bottleIds.ToList();
        if (ids.Count == 0) return new Dictionary<int, int>();
        return await _db.MhopBottleMessages.AsNoTracking()
            .Where(m => ids.Contains(m.BottleId) && m.Status == MhopBottleMessageStatus.Visible)
            .GroupBy(m => m.BottleId)
            .Select(g => new { BottleId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.BottleId, x => x.Count, cancellationToken);
    }

    /// <summary>批量取用户名（后台展示真实身份用）。</summary>
    public async Task<Dictionary<int, string>> LoadUsernamesAsync(
        IEnumerable<int> userIds, CancellationToken cancellationToken = default)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<int, string>();
        return await _db.MhopUsers.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Username, cancellationToken);
    }

}
/// <summary>漂流瓶后台统计口径。</summary>
public sealed record MhopBottleAdminStats(
    /// <summary>命中危机词且仍在漂流/对话中的瓶子数。</summary>
    int Crisis,
    /// <summary>被举报且未下架的瓶子数。</summary>
    int Reported,
    /// <summary>待人工处置（AI 未放行）的瓶子数。</summary>
    int Pending,
    /// <summary>AI 判定疑似/违规、仍在下架的瓶子数。</summary>
    int Suspect,
    /// <summary>AI 没给出结论（服务不可用/拒答）的待审瓶子数。</summary>
    int AiUnavailable,
    /// <summary>AI 标记疑似/违规且仍然可见的消息数。</summary>
    int FlaggedMessages,
    /// <summary>被隐藏的消息数（AI 自动隐藏 + 人工隐藏）。</summary>
    int HiddenMessages);
