using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application.Mapping;
using ClouderyApi.Modules.Scforge.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>评论树。</summary>
public sealed record ScforgeCommentListDto(List<ScforgeCommentDto> Items, int Total);

/// <summary>
/// 插件评论的用例编排：读取（组装回复树）、发表、编辑、删除。
///
/// 删除采用「连同子孙一起删除」的口径：父评论消失后子回复会失去上文，
/// 留在树上只会让读者困惑，因此一次性清理整棵子树。
/// </summary>
public sealed class ScforgeCommentAppService(
    IScforgeDbContext db,
    ScforgeVoteAppService votes,
    ILogger<ScforgeCommentAppService> logger)
{
    public async Task<ScforgeCommentListDto> ListAsync(
        Guid pluginId,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        var exists = await db.ScforgePlugins
            .AsNoTracking()
            .AnyAsync(p => p.Id == pluginId && p.Status == ScforgeContentStatus.Published, cancellationToken);
        if (!exists) throw new ScforgeApiException(404, "插件不存在或已被删除");

        var comments = await db.ScforgeComments
            .AsNoTracking()
            .Where(c => c.PluginId == pluginId)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var myVotes = await votes.LoadVotesAsync(
            actor.UserId,
            ScforgeVoteTargets.Comment,
            comments.Select(c => c.Id).ToList(),
            cancellationToken);

        var tree = ScforgeMapper.ToCommentTree(comments, actor.UserId, admin.IsAdmin, myVotes);
        return new ScforgeCommentListDto(tree, comments.Count);
    }

    public async Task<ScforgeCommentDto> CreateAsync(
        Guid pluginId,
        ScforgeCommentCreateIn body,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.RequireUserId();

        var plugin = await db.ScforgePlugins
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken)
            ?? throw new ScforgeApiException(404, "插件不存在或已被删除");
        // 未通过审核的插件不接受公众评论；作者与管理员可以留内部讨论。
        if (plugin.Status != ScforgeContentStatus.Published && !actor.IsAuthorOf(plugin.AuthorId) && !admin.IsAdmin)
        {
            throw new ScforgeApiException(404, "插件不存在或已被删除");
        }

        var text = (body.Body ?? string.Empty).Trim();
        ValidateBody(text);

        Guid? parentId = null;
        if (!string.IsNullOrWhiteSpace(body.ParentId))
        {
            if (!Guid.TryParse(body.ParentId.Trim(), out var parsed))
            {
                throw new ScforgeRuleException("父评论标识不合法");
            }

            var parent = await db.ScforgeComments
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == parsed && c.PluginId == pluginId, cancellationToken)
                ?? throw new ScforgeApiException(404, "要回复的评论不存在");
            parentId = parent.Id;
        }

        var now = DateTime.UtcNow;
        var comment = new ScforgeComment
        {
            Id = Guid.NewGuid(),
            PluginId = pluginId,
            ParentId = parentId,
            Body = text,
            AuthorId = userId,
            AuthorName = actor.DisplayName,
            AuthorAvatar = string.IsNullOrWhiteSpace(actor.Avatar) ? null : actor.Avatar,
            CreatedAt = now,
        };

        db.ScforgeComments.Add(comment);

        // CommentCount 与列表一致性由服务端维护（评论不在外键级联链上）。
        var tracked = await db.ScforgePlugins.FirstAsync(p => p.Id == pluginId, cancellationToken);
        tracked.CommentCount += 1;
        tracked.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SCForge：{Author} 评论了插件 {PluginId}", actor.DisplayName, pluginId);

        return ScforgeMapper.ToCommentTree([comment], userId, admin.IsAdmin, new Dictionary<Guid, int>()).Single();
    }

    public async Task<ScforgeCommentDto> UpdateAsync(
        Guid commentId,
        ScforgeCommentUpdateIn body,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.RequireUserId();

        var comment = await db.ScforgeComments.FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken)
                      ?? throw new ScforgeApiException(404, "评论不存在或已被删除");
        if (comment.AuthorId != userId) throw new ScforgeApiException(403, "只能编辑自己的评论");

        var text = (body.Body ?? string.Empty).Trim();
        ValidateBody(text);

        comment.Body = text;
        comment.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ScforgeMapper.ToCommentTree(
            [comment],
            userId,
            admin.IsAdmin,
            new Dictionary<Guid, int> { [comment.Id] = 0 }).Single();
    }

    /// <summary>删除一条评论及其全部子孙回复，并同步插件的评论计数。</summary>
    public async Task DeleteAsync(
        Guid commentId,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.RequireUserId();

        var comment = await db.ScforgeComments.FirstOrDefaultAsync(c => c.Id == commentId, cancellationToken)
                      ?? throw new ScforgeApiException(404, "评论不存在或已被删除");
        if (comment.AuthorId != userId && !admin.IsAdmin)
        {
            throw new ScforgeApiException(403, "只能删除自己的评论");
        }

        var pluginId = comment.PluginId;

        var all = await db.ScforgeComments
            .Where(c => c.PluginId == pluginId)
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync(cancellationToken);

        var doomed = CollectSubtree(all.Select(a => (a.Id, a.ParentId)).ToList(), commentId);

        await db.ScforgeComments.Where(c => doomed.Contains(c.Id)).ExecuteDeleteAsync(cancellationToken);
        await db.ScforgeVotes
            .Where(v => v.TargetType == ScforgeVoteTargets.Comment && doomed.Contains(v.TargetId))
            .ExecuteDeleteAsync(cancellationToken);

        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken);
        if (plugin is not null)
        {
            plugin.CommentCount = Math.Max(0, plugin.CommentCount - doomed.Count);
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("SCForge：{Author} 删除了 {Count} 条评论（插件 {PluginId}）", actor.DisplayName, doomed.Count, pluginId);
    }

    private static void ValidateBody(string text)
    {
        if (text.Length == 0) throw new ScforgeRuleException("评论内容不能为空");
        if (text.Length > ScforgeCatalog.MaxCommentLength)
        {
            throw new ScforgeRuleException($"评论不能超过 {ScforgeCatalog.MaxCommentLength} 个字符");
        }
    }

    /// <summary>广度优先收集子树（含自身）；父子关系损坏时按「只有自己」处理，不会丢数据。</summary>
    private static List<Guid> CollectSubtree(List<(Guid Id, Guid? ParentId)> all, Guid root)
    {
        var result = new List<Guid> { root };
        var frontier = new HashSet<Guid> { root };

        while (frontier.Count > 0)
        {
            var next = new HashSet<Guid>();
            foreach (var (id, parentId) in all)
            {
                if (parentId is { } parent && frontier.Contains(parent) && !result.Contains(id))
                {
                    result.Add(id);
                    next.Add(id);
                }
            }

            frontier = next;
        }

        return result;
    }
}
