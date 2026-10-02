using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Api.Contracts;
using ClouderyApi.Modules.Mhop.Infrastructure;

namespace ClouderyApi.Modules.Mhop.Application.Mapping;

/// <summary>
/// 管理后台响应映射（纯函数）：帖子 / 回复 / 用户 / AI 日志实体 → 输出 DTO。
/// 不含 HTTP、EF 与业务编排，供 AdminAppService 组装响应。
/// </summary>
public static class MhopAdminMapper
{
    /// <summary>后台视角取真实作者：匿名帖也返回作者与手机号，历史匿名帖 user_id 丢失则为 null。</summary>
    public static (string? Author, string? Phone) RealAuthor(
        int? userId, IReadOnlyDictionary<int, MhopUser> users)
    {
        if (!userId.HasValue) return (null, null);
        if (!users.TryGetValue(userId.Value, out var user)) return ("未知用户", null);
        return (user.Username, string.IsNullOrEmpty(user.Phone) ? null : user.Phone);
    }

    public static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];

    public static string PostExcerpt(string content)
        => content.Length > 80 ? content[..80] + "…" : content;

    public static AdminPostListItemOut ToPostListItem(
        MhopPost post, IReadOnlyDictionary<int, MhopUser> users, int replyCount)
    {
        var (author, phone) = RealAuthor(post.UserId, users);
        return new AdminPostListItemOut
        {
            Id = post.Id,
            Content = post.Content,
            Board = post.Board,
            Status = post.Status,
            Crisis = post.Crisis,
            IsAnonymous = post.IsAnonymous,
            Author = author,
            AuthorPhone = phone,
            ReviewNote = post.ReviewNote ?? string.Empty,
            AiFlag = post.AiFlag ?? string.Empty,
            AiReviewNote = post.AiReviewNote ?? string.Empty,
            AiReviewedAt = post.AiReviewedAt,
            ReplyCount = replyCount,
            CreatedAt = post.CreatedAt,
        };
    }

    public static AdminReplyListItemOut ToReplyListItem(
        MhopReply reply, IReadOnlyDictionary<int, MhopUser> users, string postContent)
    {
        var (author, phone) = RealAuthor(reply.UserId, users);
        return new AdminReplyListItemOut
        {
            Id = reply.Id,
            PostId = reply.PostId,
            PostExcerpt = PostExcerpt(postContent),
            Content = reply.Content,
            Status = reply.Status,
            IsAi = reply.IsAi,
            Crisis = reply.Crisis,
            IsAnonymous = reply.IsAnonymous,
            Author = author,
            AuthorPhone = phone,
            ReviewNote = reply.ReviewNote ?? string.Empty,
            AiFlag = reply.AiFlag ?? string.Empty,
            AiReviewNote = reply.AiReviewNote ?? string.Empty,
            AiReviewedAt = reply.AiReviewedAt,
            Recalled = reply.Recalled,
            RecallReason = reply.RecallReason ?? string.Empty,
            CreatedAt = reply.CreatedAt,
        };
    }

    public static AdminUserListItemOut ToUserListItem(MhopUser user, int postCount, int replyCount)
    {
        var item = MhopAuthMapper.ToUserOut(user);
        var isSuper = MhopAdminPermissions.IsSuper(user.Role);
        return new AdminUserListItemOut
        {
            Id = item.Id,
            Username = item.Username,
            Email = item.Email,
            Phone = item.Phone,
            Role = item.Role,
            Status = item.Status,
            Avatar = item.Avatar,
            Badge = item.Badge,
            // 超管隐式全权限，列表中用空数组 + is_super 表达，与独立后端版本保持一致
            Permissions = isSuper ? new List<string>() : item.Permissions,
            IsSuper = isSuper,
            CreatedAt = item.CreatedAt,
            PostCount = postCount,
            ReplyCount = replyCount,
        };
    }

    public static AdminAiLogItemOut ToAiLogItem(MhopAiLog log, MhopReply? reply)
        => new()
        {
            Id = log.Id,
            Module = log.Module,
            Engine = log.Engine,
            Prompt = Truncate(log.Prompt ?? string.Empty, 300),
            Response = Truncate(log.Response ?? string.Empty, 600),
            CreatedAt = log.CreatedAt,
            ReplyId = reply?.Id,
            PostId = reply?.PostId,
            ReplyStatus = reply?.Status,
            Recalled = reply?.Recalled ?? false,
            RecallReason = reply?.RecallReason ?? string.Empty,
        };

    public static AdminPermissionOptionOut ToPermissionOption(string code)
        => new() { Code = code, Name = MhopAdminPermissions.Labels[code] };
}
