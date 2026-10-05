using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>投票方向。</summary>
public enum ScforgeVoteDirection
{
    /// <summary>保持不变（幂等点击）。</summary>
    Keep = 0,

    Up = 1,

    Down = -1,

    /// <summary>撤销已有投票。</summary>
    Clear = 2,
}

/// <summary>
/// 插件与评论的投票编排。
///
/// 计数不靠自增：每次都按 <c>scforge_votes</c> 重算目标的两列计数后再保存，
/// 因此并发重复投票不会让计数漂移（唯一索引保证一个用户只有一票）。
/// </summary>
public sealed class ScforgeVoteAppService(
    IScforgeDbContext db,
    ScforgeAccessAppService access,
    ILogger<ScforgeVoteAppService> logger)
{
    public async Task<ScforgeVoteStateDto> VotePluginAsync(
        Guid pluginId,
        ScforgeVoteDirection direction,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.RequireUserId();

        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");

        // 投票同样是隐私面：给一个看不到的插件点赞等于公开确认它存在，
        // 也让作者能从赞同数反推出有多少人在用。
        var unlocked = access.IsUnlocked(plugin, accessToken);
        if (!await access.CanAccessAsync(plugin, actor, admin, unlocked, cancellationToken))
        {
            ScforgeAccessAppService.Deny(plugin);
        }

        await ApplyAsync(userId, ScforgeVoteTargets.Plugin, pluginId, direction, cancellationToken);
        await RecalculatePluginAsync(plugin, cancellationToken);

        return new ScforgeVoteStateDto
        {
            Upvotes = plugin.Upvotes,
            Downvotes = plugin.Downvotes,
            Score = plugin.Score,
            MyVote = await ReadVoteAsync(userId, ScforgeVoteTargets.Plugin, pluginId, cancellationToken),
        };
    }

    public async Task<ScforgeVoteStateDto> VoteCommentAsync(
        Guid commentId,
        ScforgeVoteDirection direction,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.RequireUserId();

        var comment = await db.ScforgeComments.FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken)
                      ?? throw new ScforgeApiException(404, "评论不存在或已被删除");

        await ApplyAsync(userId, ScforgeVoteTargets.Comment, commentId, direction, cancellationToken);
        await RecalculateCommentAsync(comment, cancellationToken);

        return new ScforgeVoteStateDto
        {
            Upvotes = comment.Upvotes,
            Downvotes = comment.Downvotes,
            Score = comment.Score,
            MyVote = await ReadVoteAsync(userId, ScforgeVoteTargets.Comment, commentId, cancellationToken),
        };
    }

    /// <summary>当前用户对某目标的投票状态（未登录一律 0）。</summary>
    public async Task<int> ReadVoteAsync(
        string? userId,
        string targetType,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId)) return 0;
        var vote = await db.ScforgeVotes
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.UserId == userId && v.TargetType == targetType && v.TargetId == targetId, cancellationToken);
        return vote?.Value ?? 0;
    }

    /// <summary>批量读取当前用户对一组目标的投票（列表页避免 N+1）。</summary>
    public async Task<Dictionary<Guid, int>> LoadVotesAsync(
        string? userId,
        string targetType,
        IReadOnlyCollection<Guid> targetIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(userId) || targetIds.Count == 0) return [];
        return await db.ScforgeVotes
            .AsNoTracking()
            .Where(v => v.UserId == userId && v.TargetType == targetType && targetIds.Contains(v.TargetId))
            .ToDictionaryAsync(v => v.TargetId, v => v.Value, cancellationToken);
    }

    /// <summary>写入或清除一票（唯一索引让「重复点同一方向」自然幂等）。</summary>
    private async Task ApplyAsync(
        string userId,
        string targetType,
        Guid targetId,
        ScforgeVoteDirection direction,
        CancellationToken cancellationToken)
    {
        var existing = await db.ScforgeVotes
            .FirstOrDefaultAsync(v => v.UserId == userId && v.TargetType == targetType && v.TargetId == targetId, cancellationToken);

        if (direction is ScforgeVoteDirection.Clear or ScforgeVoteDirection.Keep)
        {
            if (existing is not null) db.ScforgeVotes.Remove(existing);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        var value = direction == ScforgeVoteDirection.Up ? 1 : -1;

        if (existing is null)
        {
            db.ScforgeVotes.Add(new ScforgeVote
            {
                UserId = userId,
                TargetType = targetType,
                TargetId = targetId,
                Value = value,
            });
        }
        else if (existing.Value != value)
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>按投票表重算插件的赞同 / 反对计数（权威口径，容忍并发）。</summary>
    private async Task RecalculatePluginAsync(ScforgePlugin plugin, CancellationToken cancellationToken)
    {
        var tally = await db.ScforgeVotes
            .AsNoTracking()
            .Where(v => v.TargetType == ScforgeVoteTargets.Plugin && v.TargetId == plugin.Id)
            .GroupBy(v => v.Value)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        plugin.Upvotes = tally.FirstOrDefault(t => t.Value == 1)?.Count ?? 0;
        plugin.Downvotes = tally.FirstOrDefault(t => t.Value == -1)?.Count ?? 0;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task RecalculateCommentAsync(ScforgeComment comment, CancellationToken cancellationToken)
    {
        var tally = await db.ScforgeVotes
            .AsNoTracking()
            .Where(v => v.TargetType == ScforgeVoteTargets.Comment && v.TargetId == comment.Id)
            .GroupBy(v => v.Value)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        comment.Upvotes = tally.FirstOrDefault(t => t.Value == 1)?.Count ?? 0;
        comment.Downvotes = tally.FirstOrDefault(t => t.Value == -1)?.Count ?? 0;
        await db.SaveChangesAsync(cancellationToken);

        if (logger.IsEnabled(LogLevel.Trace))
        {
            logger.LogTrace("评论 {CommentId} 投票重算：+{Up} / -{Down}", comment.Id, comment.Upvotes, comment.Downvotes);
        }
    }
}
