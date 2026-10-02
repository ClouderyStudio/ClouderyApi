using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Api.Contracts;

namespace ClouderyApi.Modules.Mhop.Application.Mapping;

/// <summary>
/// 论坛响应映射（纯函数）：帖子 / 回复实体与作者信息 → 输出 DTO。
/// 不含 HTTP、EF 与业务编排，供 ForumAppService 组装响应。
/// </summary>
public static class MhopForumMapper
{
    public static List<string> ParseImages(string? raw) => MhopImageRefs.Parse(raw);

    public static (string Author, string Avatar, string Badge) AuthorForPost(
        MhopPost post, IReadOnlyDictionary<int, MhopUser> users)
    {
        if (!post.IsAnonymous && post.UserId.HasValue)
        {
            if (users.TryGetValue(post.UserId.Value, out var user))
                return (user.Username, user.Avatar ?? string.Empty, user.Badge ?? string.Empty);
            return ("实名用户", string.Empty, string.Empty);
        }
        return ("匿名朋友", string.Empty, string.Empty);
    }

    public static (string Author, string Avatar, string Badge) AuthorForReply(
        MhopReply reply, IReadOnlyDictionary<int, MhopUser> users)
    {
        if (reply.IsAi) return ("AI 心理助手", string.Empty, string.Empty);
        if (!reply.IsAnonymous && reply.UserId.HasValue)
        {
            if (users.TryGetValue(reply.UserId.Value, out var user))
                return (user.Username, user.Avatar ?? string.Empty, user.Badge ?? string.Empty);
            return ("实名用户", string.Empty, string.Empty);
        }
        return ("匿名朋友", string.Empty, string.Empty);
    }

    public static ReplyOut ToReplyOut(
        MhopReply reply,
        IReadOnlyDictionary<int, MhopUser> users,
        IReadOnlyDictionary<int, int>? likeCounts,
        IReadOnlySet<int>? liked)
    {
        var (author, avatar, badge) = AuthorForReply(reply, users);
        return new ReplyOut
        {
            Id = reply.Id,
            PostId = reply.PostId,
            Content = reply.Recalled ? string.Empty : reply.Content,
            Status = reply.Status,
            IsAi = reply.IsAi,
            IsAnonymous = reply.IsAnonymous,
            Author = author,
            AuthorAvatar = avatar,
            AuthorBadge = badge,
            Crisis = reply.Crisis,
            Recalled = reply.Recalled,
            RecallReason = reply.RecallReason ?? string.Empty,
            LikeCount = likeCounts?.GetValueOrDefault(reply.Id, 0) ?? 0,
            Liked = liked?.Contains(reply.Id) ?? false,
            Images = ParseImages(reply.Images),
            CreatedAt = reply.CreatedAt,
        };
    }

    public static PostOut ToPostOut(
        MhopPost post,
        IReadOnlyDictionary<int, MhopUser> users,
        IReadOnlyCollection<MhopReply> replies,
        int? currentUserId,
        IReadOnlyDictionary<int, int>? likeCounts,
        IReadOnlySet<int>? liked)
    {
        var visible = replies.Where(r => r.Status == ContentStatus.Published && !r.Recalled).ToList();
        var last = visible.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
        var (author, avatar, badge) = AuthorForPost(post, users);
        var lastAuthor = last is null ? string.Empty : AuthorForReply(last, users).Author;

        return new PostOut
        {
            Id = post.Id,
            Content = post.Content,
            Board = post.Board,
            Status = post.Status,
            Crisis = post.Crisis,
            IsAnonymous = post.IsAnonymous,
            Author = author,
            AuthorAvatar = avatar,
            AuthorBadge = badge,
            ReplyCount = visible.Count,
            ViewCount = post.ViewCount,
            AiReplied = visible.Any(r => r.IsAi),
            Mine = currentUserId.HasValue && post.UserId == currentUserId.Value,
            LikeCount = likeCounts?.GetValueOrDefault(post.Id, 0) ?? 0,
            Liked = liked?.Contains(post.Id) ?? false,
            Images = ParseImages(post.Images),
            LastReplyAt = last?.CreatedAt,
            LastReplyAuthor = lastAuthor,
            CreatedAt = post.CreatedAt,
        };
    }

    public static void CopyPostFields(PostOut target, PostOut source)
    {
        target.Id = source.Id;
        target.Content = source.Content;
        target.Board = source.Board;
        target.Status = source.Status;
        target.Crisis = source.Crisis;
        target.IsAnonymous = source.IsAnonymous;
        target.Author = source.Author;
        target.AuthorAvatar = source.AuthorAvatar;
        target.AuthorBadge = source.AuthorBadge;
        target.ReplyCount = source.ReplyCount;
        target.ViewCount = source.ViewCount;
        target.AiReplied = source.AiReplied;
        target.Mine = source.Mine;
        target.LikeCount = source.LikeCount;
        target.Liked = source.Liked;
        target.Images = source.Images;
        target.LastReplyAt = source.LastReplyAt;
        target.LastReplyAuthor = source.LastReplyAuthor;
        target.CreatedAt = source.CreatedAt;
    }
}
