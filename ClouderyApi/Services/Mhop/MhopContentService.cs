using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 内容删除的公共实现：作者自助删除与管理员删除共用同一套回收逻辑，避免两处实现漂移导致漏清数据
/// （帖子 → 回复 / 点赞 / AI 日志 / 图片；回复 → 点赞 / AI 日志 / 图片；用户 → 其全部内容）。
/// 统一先落库、再尽力清理对象存储，删库失败时不会先把图片删掉。
/// </summary>
public sealed class MhopContentService
{
    private readonly MhopDbContext _db;
    private readonly MhopUploadService _uploads;
    private readonly ILogger<MhopContentService> _logger;

    public MhopContentService(MhopDbContext db, MhopUploadService uploads, ILogger<MhopContentService> logger)
    {
        _db = db;
        _uploads = uploads;
        _logger = logger;
    }

    /// <summary>删除帖子及其全部回复、点赞、AI 日志与图片。返回被一并删除的回复数。</summary>
    public async Task<int> DeletePostAsync(MhopPost post, CancellationToken cancellationToken = default)
    {
        var replies = await _db.MhopReplies.Where(r => r.PostId == post.Id).ToListAsync(cancellationToken);
        var replyIds = replies.Select(r => r.Id).ToList();

        var likes = await _db.MhopLikes.Where(l =>
            (l.TargetType == "post" && l.TargetId == post.Id)
            || (replyIds.Count > 0 && l.TargetType == "reply" && replyIds.Contains(l.TargetId)))
            .ToListAsync(cancellationToken);

        var logs = replyIds.Count == 0
            ? new List<MhopAiLog>()
            : await _db.MhopAiLogs
                .Where(l => l.ReplyId.HasValue && replyIds.Contains(l.ReplyId.Value))
                .ToListAsync(cancellationToken);

        // 帖子与它全部回复的图片一起收集，落库成功后再清理存储对象
        var images = MhopImageRefs.Parse(post.Images)
            .Concat(replies.SelectMany(r => MhopImageRefs.Parse(r.Images)))
            .ToList();

        if (likes.Count > 0) _db.MhopLikes.RemoveRange(likes);
        if (logs.Count > 0) _db.MhopAiLogs.RemoveRange(logs);
        if (replies.Count > 0) _db.MhopReplies.RemoveRange(replies);
        _db.MhopPosts.Remove(post);
        await _db.SaveChangesAsync(cancellationToken);

        await _uploads.DeleteAsync(images, cancellationToken);
        _logger.LogInformation("已删除帖子 {PostId}：连带 {Replies} 条回复、{Images} 张图片",
            post.Id, replies.Count, images.Count);
        return replies.Count;
    }

    /// <summary>删除回复及其点赞、AI 日志与图片。</summary>
    public async Task DeleteReplyAsync(MhopReply reply, CancellationToken cancellationToken = default)
    {
        var images = MhopImageRefs.Parse(reply.Images);

        _db.MhopLikes.RemoveRange(await _db.MhopLikes
            .Where(l => l.TargetType == "reply" && l.TargetId == reply.Id).ToListAsync(cancellationToken));
        _db.MhopAiLogs.RemoveRange(await _db.MhopAiLogs
            .Where(l => l.ReplyId == reply.Id).ToListAsync(cancellationToken));
        _db.MhopReplies.Remove(reply);
        await _db.SaveChangesAsync(cancellationToken);

        await _uploads.DeleteAsync(images, cancellationToken);
        _logger.LogInformation("已删除回复 {ReplyId}：连带 {Images} 张图片", reply.Id, images.Count);
    }

    /// <summary>删除用户及其全部内容：名下帖子（含他人对其帖子的回复）、名下回复、点赞、AI 日志与图片。</summary>
    public async Task<(int Posts, int Replies)> DeleteUserAsync(MhopUser user, CancellationToken cancellationToken = default)
    {
        var posts = await _db.MhopPosts.Where(p => p.UserId == user.Id).ToListAsync(cancellationToken);
        var postIds = posts.Select(p => p.Id).ToList();

        // 名下回复 + 自己帖子下所有人的回复都要一并清掉，否则会留下挂空回复
        var replies = await _db.MhopReplies
            .Where(r => r.UserId == user.Id || (postIds.Count > 0 && postIds.Contains(r.PostId)))
            .ToListAsync(cancellationToken);
        var replyIds = replies.Select(r => r.Id).ToList();

        var likes = await _db.MhopLikes
            .Where(l => l.UserId == user.Id
                        || (postIds.Count > 0 && l.TargetType == "post" && postIds.Contains(l.TargetId))
                        || (replyIds.Count > 0 && l.TargetType == "reply" && replyIds.Contains(l.TargetId)))
            .ToListAsync(cancellationToken);

        var logs = await _db.MhopAiLogs
            .Where(l => l.UserId == user.Id
                        || (replyIds.Count > 0 && l.ReplyId.HasValue && replyIds.Contains(l.ReplyId.Value)))
            .ToListAsync(cancellationToken);

        var images = posts.SelectMany(p => MhopImageRefs.Parse(p.Images))
            .Concat(replies.SelectMany(r => MhopImageRefs.Parse(r.Images)))
            .Append(user.Avatar ?? string.Empty)
            .ToList();

        if (likes.Count > 0) _db.MhopLikes.RemoveRange(likes);
        if (logs.Count > 0) _db.MhopAiLogs.RemoveRange(logs);
        if (replies.Count > 0) _db.MhopReplies.RemoveRange(replies);
        if (posts.Count > 0) _db.MhopPosts.RemoveRange(posts);
        _db.MhopUsers.Remove(user);
        await _db.SaveChangesAsync(cancellationToken);

        await _uploads.DeleteAsync(images, cancellationToken);
        _logger.LogInformation("已删除用户 {UserId}：连带 {Posts} 篇帖子、{Replies} 条回复、{Images} 张图片",
            user.Id, posts.Count, replies.Count, images.Count);
        return (posts.Count, replies.Count);
    }
}
