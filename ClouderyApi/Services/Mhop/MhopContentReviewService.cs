using ClouderyApi.Data;
using ClouderyApi.Modules.Mhop.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 内容 AI 自动审核调度：帖子 / 回复 / 漂流瓶瓶身 / 漂流瓶会话消息。
/// 结论口径：
///   safe        → 帖子/回复/瓶身自动公开；消息保持可见并清空标记；
///   suspect     → 转人工；瓶身保持待审核（不进海），消息保持可见但打标；
///   violation   → 转人工；瓶身保持待审核，消息**自动隐藏**（先隐藏后人审，不给违规内容传播窗口）；
///   unavailable → AI 没有给出结论（未配置 / 调用失败），一律转人工，绝不自动公开。
/// 其它约定：
///   * 全部在后台独立 DI 作用域执行，不阻塞发布请求；
///   * 写入都用「Id + 内容未变」条件更新，避免覆盖用户改写或人工处置的结果；
///   * 人工放行过的内容标记为 <see cref="MhopModerationOutcome.Approved"/>，后续重跑 AI 只更新理由、不再翻案；
///   * 单实例内限制并发大模型调用数，避免刷量把审核队列打爆。
/// </summary>
public sealed class MhopContentReviewService
{
    /// <summary>同一时刻允许在途的大模型审核调用数（超出排队）。</summary>
    private const int MaxConcurrentAiCalls = 4;

    private static readonly SemaphoreSlim AiGate = new(MaxConcurrentAiCalls, MaxConcurrentAiCalls);

    private readonly MhopAiService _ai;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<MhopContentReviewService> _logger;

    public MhopContentReviewService(
        MhopAiService ai,
        IServiceScopeFactory scopes,
        ILogger<MhopContentReviewService> logger)
    {
        _ai = ai;
        _scopes = scopes;
        _logger = logger;
    }

    public void QueuePostReview(int postId) => _ = Task.Run(() => ReviewPostAsync(postId));

    public void QueueReplyReview(int replyId) => _ = Task.Run(() => ReviewReplyAsync(replyId));

    /// <summary>排队审核瓶身。<paramref name="rescreen"/> 为 true 时允许对已审核（含已公开）的瓶子重跑。</summary>
    public void QueueBottleReview(int bottleId, bool rescreen = false)
        => _ = Task.Run(() => ReviewBottleAsync(bottleId, rescreen));

    /// <summary>排队审核会话消息。<paramref name="rescreen"/> 为 true 时允许对已审核过的消息重跑。</summary>
    public void QueueMessageReview(int messageId, bool rescreen = false)
        => _ = Task.Run(() => ReviewMessageAsync(messageId, rescreen));

    // ---------------- 帖子 / 回复 ----------------

    private async Task ReviewPostAsync(int postId)
    {
        try
        {
            MhopPost? post;
            using (var scope = _scopes.CreateScope())
            {
                post = await scope.ServiceProvider.GetRequiredService<MhopDbContext>()
                    .MhopPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId);
            }
            if (post is null || post.Status != ContentStatus.Pending) return;

            var outcome = await ModerateAsync(post.Content, MhopContentKind.Post);
            using var writeScope = _scopes.CreateScope();
            var writeDb = writeScope.ServiceProvider.GetRequiredService<MhopDbContext>();

            if (outcome.IsSafe)
            {
                var affected = await writeDb.MhopPosts
                    .Where(p => p.Id == postId && p.Status == ContentStatus.Pending && p.Content == post.Content)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, ContentStatus.Published)
                        .SetProperty(p => p.AiFlag, string.Empty)
                        .SetProperty(p => p.AiReviewNote, string.Empty)
                        .SetProperty(p => p.AiReviewedAt, DateTime.UtcNow));
                if (affected == 1)
                {
                    _logger.LogInformation("帖子 {PostId} AI 审核通过，已自动公开", postId);
                    await _ai.EnsureForumReplyAsync(postId, post.Content, post.Crisis);
                }
                return;
            }

            var note = BuildNote(outcome);
            await writeDb.MhopPosts
                .Where(p => p.Id == postId && p.Status == ContentStatus.Pending && p.Content == post.Content)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(p => p.AiFlag, outcome.Verdict)
                    .SetProperty(p => p.AiReviewNote, note)
                    .SetProperty(p => p.AiReviewedAt, DateTime.UtcNow));
            _logger.LogInformation("帖子 {PostId} AI 审核未通过（{Verdict}），转人工：{Reason}", postId, outcome.Verdict, note);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "帖子 {PostId} AI 自动审核失败", postId);
        }
    }

    private async Task ReviewReplyAsync(int replyId)
    {
        try
        {
            MhopReply? reply;
            using (var scope = _scopes.CreateScope())
            {
                reply = await scope.ServiceProvider.GetRequiredService<MhopDbContext>()
                    .MhopReplies.AsNoTracking().FirstOrDefaultAsync(r => r.Id == replyId);
            }
            if (reply is null || reply.Status != ContentStatus.Pending) return;

            var outcome = await ModerateAsync(reply.Content, MhopContentKind.Reply);
            using var writeScope = _scopes.CreateScope();
            var writeDb = writeScope.ServiceProvider.GetRequiredService<MhopDbContext>();

            if (outcome.IsSafe)
            {
                await writeDb.MhopReplies
                    .Where(r => r.Id == replyId && r.Status == ContentStatus.Pending && r.Content == reply.Content)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.Status, ContentStatus.Published)
                        .SetProperty(r => r.AiFlag, string.Empty)
                        .SetProperty(r => r.AiReviewNote, string.Empty)
                        .SetProperty(r => r.AiReviewedAt, DateTime.UtcNow));
                _logger.LogInformation("回复 {ReplyId} AI 审核通过，已自动公开", replyId);
                return;
            }

            var note = BuildNote(outcome);
            await writeDb.MhopReplies
                .Where(r => r.Id == replyId && r.Status == ContentStatus.Pending && r.Content == reply.Content)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.AiFlag, outcome.Verdict)
                    .SetProperty(r => r.AiReviewNote, note)
                    .SetProperty(r => r.AiReviewedAt, DateTime.UtcNow));
            _logger.LogInformation("回复 {ReplyId} AI 审核未通过（{Verdict}），转人工：{Reason}", replyId, outcome.Verdict, note);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "回复 {ReplyId} AI 自动审核失败", replyId);
        }
    }

    // ---------------- 漂流瓶瓶身 ----------------

    private async Task ReviewBottleAsync(int bottleId, bool rescreen)
    {
        try
        {
            MhopBottle? bottle;
            using (var scope = _scopes.CreateScope())
            {
                bottle = await scope.ServiceProvider.GetRequiredService<MhopDbContext>()
                    .MhopBottles.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bottleId);
            }
            if (bottle is null) return;
            // 首次审核只处理「待审核」；重跑审核允许已经是漂流中/对话中的瓶子
            if (!rescreen && bottle.Status != MhopBottleStatus.Pending) return;
            if (rescreen && bottle.Status == MhopBottleStatus.Removed) return;

            var outcome = await ModerateAsync(bottle.Content, MhopContentKind.Bottle);
            // 只有仍停留在待审核的瓶子才允许被本次结论放行（人工可能已经处置过）
            var canPublish = bottle.Status == MhopBottleStatus.Pending;

            using var writeScope = _scopes.CreateScope();
            var writeDb = writeScope.ServiceProvider.GetRequiredService<MhopDbContext>();

            if (outcome.IsSafe)
            {
                if (canPublish)
                {
                    var affected = await writeDb.MhopBottles
                        .Where(b => b.Id == bottleId && b.Status == MhopBottleStatus.Pending && b.Content == bottle.Content)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.Status, MhopBottleStatus.Drifting)
                            .SetProperty(b => b.AiFlag, string.Empty)
                            .SetProperty(b => b.AiReviewNote, string.Empty)
                            .SetProperty(b => b.AiReviewedAt, DateTime.UtcNow));
                    if (affected == 1) _logger.LogInformation("漂流瓶 {BottleId} AI 审核通过，已放入海中", bottleId);
                }
                else
                {
                    // 重跑后转为通过：只清理标记，公开状态交给人工决定
                    await writeDb.MhopBottles
                        .Where(b => b.Id == bottleId && b.Content == bottle.Content)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.AiFlag, string.Empty)
                            .SetProperty(b => b.AiReviewNote, string.Empty)
                            .SetProperty(b => b.AiReviewedAt, DateTime.UtcNow));
                    _logger.LogInformation("漂流瓶 {BottleId} 重跑 AI 审核通过，已清理标记", bottleId);
                }
                return;
            }

            var note = BuildNote(outcome);
            await writeDb.MhopBottles
                .Where(b => b.Id == bottleId && b.Content == bottle.Content)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.AiFlag, outcome.Verdict)
                    .SetProperty(b => b.AiReviewNote, note)
                    .SetProperty(b => b.AiReviewedAt, DateTime.UtcNow));
            _logger.LogInformation("漂流瓶 {BottleId} AI 审核未通过（{Verdict}），转人工：{Reason}", bottleId, outcome.Verdict, note);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "漂流瓶 {BottleId} AI 自动审核失败", bottleId);
            await MarkBottleUnavailableAsync(bottleId);
        }
    }

    // ---------------- 漂流瓶会话消息 ----------------

    /// <summary>
    /// 消息 AI 审核。与瓶身不同：消息已经发出去，所以违规的处置是「立即隐藏」而不是「拦在门外」。
    /// 人工放行过（<see cref="MhopModerationOutcome.Approved"/>）的消息不再被隐藏，只更新理由留痕。
    /// </summary>
    private async Task ReviewMessageAsync(int messageId, bool rescreen)
    {
        try
        {
            MhopBottleMessage? message;
            using (var scope = _scopes.CreateScope())
            {
                message = await scope.ServiceProvider.GetRequiredService<MhopDbContext>()
                    .MhopBottleMessages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == messageId);
            }
            if (message is null) return;
            if (!rescreen && message.AiReviewedAt is not null) return;

            var outcome = await ModerateAsync(message.Content, MhopContentKind.Message);
            var approved = message.AiFlag == MhopModerationOutcome.Approved;
            var hide = outcome.Verdict == MhopModerationOutcome.Violation && !approved;

            using var writeScope = _scopes.CreateScope();
            var writeDb = writeScope.ServiceProvider.GetRequiredService<MhopDbContext>();

            if (outcome.IsSafe)
            {
                await writeDb.MhopBottleMessages
                    .Where(m => m.Id == messageId && m.Content == message.Content)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.AiFlag, approved ? MhopModerationOutcome.Approved : MhopModerationOutcome.None)
                        .SetProperty(m => m.AiReviewNote, string.Empty)
                        .SetProperty(m => m.AiReviewedAt, DateTime.UtcNow));
                return;
            }

            var note = BuildNote(outcome);
            await writeDb.MhopBottleMessages
                .Where(m => m.Id == messageId && m.Content == message.Content)
                .ExecuteUpdateAsync(s =>
                {
                    s.SetProperty(m => m.AiFlag, outcome.Verdict)
                        .SetProperty(m => m.AiReviewNote, note)
                        .SetProperty(m => m.AiReviewedAt, DateTime.UtcNow);
                    if (hide) s.SetProperty(m => m.Status, MhopBottleMessageStatus.Hidden);
                });
            _logger.LogInformation(
                "漂流瓶消息 AI 审核未通过（{Verdict}，{Action}）：message={MessageId} {Reason}",
                outcome.Verdict, hide ? "已自动隐藏待人工复核" : "保持可见待人工复核", messageId, note);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "漂流瓶消息 {MessageId} AI 自动审核失败", messageId);
            await MarkMessageUnavailableAsync(messageId);
        }
    }

    /// <summary>
    /// 兜底扫描：仍在待审核、却始终没拿到任何 AI 结论的瓶子（进程中断等异常导致审核任务丢失），
    /// 重新排队审核。正常路径（含调用失败）都会写入 AiReviewedAt，所以这里只会捞到真正卡住的任务。
    /// </summary>
    public async Task<int> RequeueStaleBottleReviewsAsync(TimeSpan staleAfter, int limit = 20)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            var cutoff = DateTime.UtcNow - staleAfter;
            var ids = await db.MhopBottles.AsNoTracking()
                .Where(b => b.Status == MhopBottleStatus.Pending
                    && b.AiReviewedAt == null
                    && b.CreatedAt < cutoff)
                .OrderBy(b => b.Id)
                .Take(limit)
                .Select(b => b.Id)
                .ToListAsync();
            foreach (var id in ids) QueueBottleReview(id);
            if (ids.Count > 0) _logger.LogWarning("重新排队 {Count} 个卡住的漂流瓶 AI 审核：{Ids}", ids.Count, string.Join(",", ids));
            return ids.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "扫描卡住的漂流瓶审核任务失败");
            return 0;
        }
    }

    // ---------------- 内部工具 ----------------

    /// <summary>串行闸门 + 统一入口：限制在途大模型调用数。</summary>
    private async Task<MhopModerationOutcome> ModerateAsync(string text, string kind)
    {
        await AiGate.WaitAsync();
        try
        {
            return await _ai.ModerateAsync(text, kind);
        }
        finally
        {
            AiGate.Release();
        }
    }

    /// <summary>审核任务自身异常时也要给人审留下线索：标为「不可用」而不是静默留空。</summary>
    private async Task MarkBottleUnavailableAsync(int bottleId)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            await db.MhopBottles
                .Where(b => b.Id == bottleId && b.AiFlag == string.Empty && b.Status == MhopBottleStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(b => b.AiFlag, MhopModerationOutcome.Unavailable)
                    .SetProperty(b => b.AiReviewNote, UnavailableNote)
                    .SetProperty(b => b.AiReviewedAt, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "漂流瓶 {BottleId} 写入审核失败标记时出错", bottleId);
        }
    }

    private async Task MarkMessageUnavailableAsync(int messageId)
    {
        try
        {
            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MhopDbContext>();
            await db.MhopBottleMessages
                .Where(m => m.Id == messageId && m.AiFlag == string.Empty)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.AiFlag, MhopModerationOutcome.Unavailable)
                    .SetProperty(m => m.AiReviewNote, UnavailableNote)
                    .SetProperty(m => m.AiReviewedAt, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "漂流瓶消息 {MessageId} 写入审核失败标记时出错", messageId);
        }
    }

    private const string UnavailableNote = "AI 未能给出审核结论，已转人工审核";

    private static string BuildNote(MhopModerationOutcome outcome) => outcome.Verdict switch
    {
        MhopModerationOutcome.Unavailable => UnavailableNote,
        MhopModerationOutcome.Violation => Truncate($"AI 判定违规：{Reason(outcome)}", 255),
        MhopModerationOutcome.Suspect => Truncate($"AI 疑似违规：{Reason(outcome)}", 255),
        _ => string.Empty,
    };

    private static string Reason(MhopModerationOutcome outcome)
        => string.IsNullOrWhiteSpace(outcome.Reason) ? "需人工复核" : outcome.Reason;

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
