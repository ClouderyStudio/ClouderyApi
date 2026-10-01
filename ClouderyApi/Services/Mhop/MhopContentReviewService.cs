using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 内容 AI 自动审核调度：帖子 / 回复 / 漂流瓶。
/// 通过 → 自动公开（帖子顺带生成 AI 回复）；未通过或 AI 不可用 → 保持待审核并写入 AI 理由，等待人工处置。
/// 全部在后台独立 DI 作用域执行，不阻塞发布请求；写入用条件更新避免覆盖人工/后续操作的结论。
/// </summary>
public sealed class MhopContentReviewService
{
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

    public void QueueBottleReview(int bottleId) => _ = Task.Run(() => ReviewBottleAsync(bottleId));

    private async Task ReviewPostAsync(int postId)
    {
        try
        {
            using (var scope = _scopes.CreateScope())
            {
                var post = await scope.ServiceProvider.GetRequiredService<MhopDbContext>()
                    .MhopPosts.AsNoTracking().FirstOrDefaultAsync(p => p.Id == postId);
                if (post is null || post.Status != MhopContentStatus.Pending) return;

                var outcome = await _ai.ModerateAsync(post.Content);
                using var writeScope = _scopes.CreateScope();
                var writeDb = writeScope.ServiceProvider.GetRequiredService<MhopDbContext>();

                if (outcome.IsSafe)
                {
                    var affected = await writeDb.MhopPosts
                        .Where(p => p.Id == postId && p.Status == MhopContentStatus.Pending && p.Content == post.Content)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(p => p.Status, MhopContentStatus.Published)
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
                    .Where(p => p.Id == postId && p.Status == MhopContentStatus.Pending && p.Content == post.Content)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.AiFlag, outcome.Verdict)
                        .SetProperty(p => p.AiReviewNote, note)
                        .SetProperty(p => p.AiReviewedAt, DateTime.UtcNow));
                _logger.LogInformation("帖子 {PostId} AI 审核未通过（{Verdict}），转人工：{Reason}", postId, outcome.Verdict, note);
            }
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
            using (var scope = _scopes.CreateScope())
            {
                var reply = await scope.ServiceProvider.GetRequiredService<MhopDbContext>()
                    .MhopReplies.AsNoTracking().FirstOrDefaultAsync(r => r.Id == replyId);
                if (reply is null || reply.Status != MhopContentStatus.Pending) return;

                var outcome = await _ai.ModerateAsync(reply.Content);
                using var writeScope = _scopes.CreateScope();
                var writeDb = writeScope.ServiceProvider.GetRequiredService<MhopDbContext>();

                if (outcome.IsSafe)
                {
                    await writeDb.MhopReplies
                        .Where(r => r.Id == replyId && r.Status == MhopContentStatus.Pending && r.Content == reply.Content)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(r => r.Status, MhopContentStatus.Published)
                            .SetProperty(r => r.AiFlag, string.Empty)
                            .SetProperty(r => r.AiReviewNote, string.Empty)
                            .SetProperty(r => r.AiReviewedAt, DateTime.UtcNow));
                    _logger.LogInformation("回复 {ReplyId} AI 审核通过，已自动公开", replyId);
                    return;
                }

                var note = BuildNote(outcome);
                await writeDb.MhopReplies
                    .Where(r => r.Id == replyId && r.Status == MhopContentStatus.Pending && r.Content == reply.Content)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.AiFlag, outcome.Verdict)
                        .SetProperty(r => r.AiReviewNote, note)
                        .SetProperty(r => r.AiReviewedAt, DateTime.UtcNow));
                _logger.LogInformation("回复 {ReplyId} AI 审核未通过（{Verdict}），转人工：{Reason}", replyId, outcome.Verdict, note);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "回复 {ReplyId} AI 自动审核失败", replyId);
        }
    }

    private async Task ReviewBottleAsync(int bottleId)
    {
        try
        {
            using (var scope = _scopes.CreateScope())
            {
                var bottle = await scope.ServiceProvider.GetRequiredService<MhopDbContext>()
                    .MhopBottles.AsNoTracking().FirstOrDefaultAsync(b => b.Id == bottleId);
                if (bottle is null || bottle.Status != MhopBottleStatus.Pending) return;

                var outcome = await _ai.ModerateAsync(bottle.Content);
                using var writeScope = _scopes.CreateScope();
                var writeDb = writeScope.ServiceProvider.GetRequiredService<MhopDbContext>();

                if (outcome.IsSafe)
                {
                    var affected = await writeDb.MhopBottles
                        .Where(b => b.Id == bottleId && b.Status == MhopBottleStatus.Pending && b.Content == bottle.Content)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(b => b.Status, MhopBottleStatus.Drifting)
                            .SetProperty(b => b.AiFlag, string.Empty)
                            .SetProperty(b => b.AiReviewNote, string.Empty)
                            .SetProperty(b => b.AiReviewedAt, DateTime.UtcNow));
                    if (affected == 1)
                        _logger.LogInformation("漂流瓶 {BottleId} AI 审核通过，已放入海中", bottleId);
                    return;
                }

                var note = BuildNote(outcome);
                await writeDb.MhopBottles
                    .Where(b => b.Id == bottleId && b.Status == MhopBottleStatus.Pending && b.Content == bottle.Content)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(b => b.AiFlag, outcome.Verdict)
                        .SetProperty(b => b.AiReviewNote, note)
                        .SetProperty(b => b.AiReviewedAt, DateTime.UtcNow));
                _logger.LogInformation("漂流瓶 {BottleId} AI 审核未通过（{Verdict}），转人工：{Reason}", bottleId, outcome.Verdict, note);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "漂流瓶 {BottleId} AI 自动审核失败", bottleId);
        }
    }

    private static string BuildNote(MhopModerationOutcome outcome)
        => outcome.Verdict == MhopModerationOutcome.Unavailable
            ? "AI 未能给出审核结论，已转人工审核"
            : Truncate($"AI 初筛未通过（{outcome.Verdict}）：{outcome.Reason}", 255);

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value[..max];
}
