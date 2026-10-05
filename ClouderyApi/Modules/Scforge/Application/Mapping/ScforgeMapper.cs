using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Domain;

namespace ClouderyApi.Modules.Scforge.Application.Mapping;

/// <summary>实体 → 对外 DTO 的投影。控制器不做字段拼装，全部收敛在这里。</summary>
public static class ScforgeMapper
{
    /// <summary>
    /// 版本聚合统计（列表页一次性批量查询后传入，避免 N+1）。
    /// <paramref name="PublishedCount"/> 是公开可见的版本数，<paramref name="Count"/> 是全部版本数。
    /// </summary>
    public readonly record struct VersionStat(int Count, int PublishedCount, string? LatestVersion, DateTime? LatestReleaseAt);

    /// <summary>
    /// MySQL 的 datetime 列读回来是 <see cref="DateTimeKind.Unspecified"/>，
    /// 直接序列化会丢掉 Z 后缀，前端按本地时间解析就会出现时区偏移。
    /// 这里统一按 UTC 标注（写入时用的就是 <c>DateTime.UtcNow</c>）。
    /// </summary>
    internal static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);

/// <summary>
/// 数据库存的是 UTC，对外一律换算成**北京时间（UTC+8）**输出，
/// 例如 2026-10-04T12:00:00Z → 2026-10-04T20:00:00+08:00。
/// 这样前端无论浏览器在哪个时区，拿到的都是统一的北京时间。
/// </summary>
    internal static DateTimeOffset ToBeijing(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToOffset(BeijingOffset);

    internal static DateTimeOffset? ToBeijing(DateTime? value) =>
        value is null ? null : ToBeijing(value.Value);


    public static ScforgeAuthorDto ToAuthor(string id, string username, string? avatar) => new()
    {
        Id = id,
        Username = username,
        Avatar = string.IsNullOrWhiteSpace(avatar) ? null : avatar,
    };

    /// <summary>版本投影；资源信息一并带上，后台审核队列不必再查一次库。</summary>
    public static ScforgeVersionDto ToVersion(ScforgeVersion version, ScforgePlugin plugin) => new()
    {
        Id = version.Id.ToString(),
        Version = version.Version,
        Channel = version.Channel,
        Changelog = version.Changelog,
        FileName = version.FileName,
        FileSize = version.FileSize,
        Downloads = version.Downloads,
        GameVersion = version.GameVersion,
        GameVersions = version.GameVersions,
        Dependencies = version.Dependencies,
        PublishedAt = ToBeijing(version.PublishedAt),
        DownloadUrl = $"/scforge/versions/{version.Id}/download",
        Status = ScforgeContentStatus.ToKey(version.Status),
        ReviewNote = version.ReviewNote,
        ReviewedAt = ToBeijing(version.ReviewedAt),
        ReviewedBy = version.ReviewedBy,
        AddonId = plugin.Id.ToString(),
        AddonName = plugin.Name,
        AddonSlug = plugin.Slug,
        AddonKind = plugin.Kind,
        Author = ToAuthor(plugin.AuthorId, plugin.AuthorName, plugin.AuthorAvatar),
    };

    public static ScforgeAddonSummaryDto ToSummary(ScforgePlugin plugin, VersionStat stat, int myVote) => new()
    {
        Id = plugin.Id.ToString(),
        Slug = plugin.Slug,
        Name = plugin.Name,
        Kind = plugin.Kind,
        Summary = plugin.Summary,
        IconUrl = plugin.IconUrl,
        Category = plugin.Category,
        Tags = plugin.Tags,
        GameVersion = plugin.GameVersion,
        Author = ToAuthor(plugin.AuthorId, plugin.AuthorName, plugin.AuthorAvatar),
        Downloads = plugin.Downloads,
        Upvotes = plugin.Upvotes,
        Downvotes = plugin.Downvotes,
        Score = plugin.Score,
        CommentCount = plugin.CommentCount,
        VersionCount = stat.Count,
        PublishedVersionCount = stat.PublishedCount,
        LatestVersion = stat.LatestVersion,
        LatestReleaseAt = ToBeijing(stat.LatestReleaseAt),
        PublishedAt = ToBeijing(plugin.PublishedAt),
        UpdatedAt = ToBeijing(plugin.UpdatedAt),
        Featured = plugin.Featured,
        MyVote = myVote,
        Status = ScforgeContentStatus.ToKey(plugin.Status),
        ReviewNote = plugin.ReviewNote,
        ReviewedAt = ToBeijing(plugin.ReviewedAt),
        ReviewedBy = plugin.ReviewedBy,
        AccessMode = plugin.AccessMode,
    };

    public static ScforgeAddonDetailDto ToDetail(
        ScforgePlugin plugin,
        IReadOnlyList<ScforgeVersion> versions,
        int myVote,
        bool canManage,
        bool canReview,
        bool canManageContent,
        bool hasAccess = true,
        bool accessUnlocked = false)
    {
        var published = versions.Where(v => v.Status == ScforgeContentStatus.Published).ToList();
        var latest = published.Count > 0 ? published[0] : null;
        var summary = ToSummary(
            plugin,
            new VersionStat(versions.Count, published.Count, latest?.Version, latest?.PublishedAt),
            myVote);

        return new ScforgeAddonDetailDto
        {
            Id = summary.Id,
            Slug = summary.Slug,
            Name = summary.Name,
            Kind = summary.Kind,
            Summary = summary.Summary,
            IconUrl = summary.IconUrl,
            Category = summary.Category,
            Tags = summary.Tags,
            GameVersion = summary.GameVersion,
            Author = summary.Author,
            Downloads = summary.Downloads,
            Upvotes = summary.Upvotes,
            Downvotes = summary.Downvotes,
            Score = summary.Score,
            CommentCount = summary.CommentCount,
            VersionCount = summary.VersionCount,
            PublishedVersionCount = summary.PublishedVersionCount,
            LatestVersion = summary.LatestVersion,
            LatestReleaseAt = summary.LatestReleaseAt,
            PublishedAt = summary.PublishedAt,
            UpdatedAt = summary.UpdatedAt,
            Featured = summary.Featured,
            MyVote = summary.MyVote,
            Status = summary.Status,
            ReviewNote = summary.ReviewNote,
            ReviewedAt = summary.ReviewedAt,
            ReviewedBy = summary.ReviewedBy,
            AccessMode = summary.AccessMode,
            AccessUnlocked = accessUnlocked,
            HasAccess = hasAccess,
            // 无权访问时只给最小外壳：名称、简介、图标、作者足以渲染「这是个受保护的插件」，
            // 描述 / readme / 截图 / 版本列表一律不下发 —— 脱敏必须在服务端做，
            // 靠前端隐藏等于把内容已经发到了浏览器。
            Description = hasAccess ? plugin.Description : string.Empty,
            Readme = hasAccess ? plugin.Readme : string.Empty,
            SourceUrl = hasAccess ? plugin.SourceUrl : null,
            IssuesUrl = hasAccess ? plugin.IssuesUrl : null,
            License = hasAccess ? plugin.License : null,
            LicenseUrl = hasAccess ? plugin.LicenseUrl : null,
            DonationUrl = hasAccess ? plugin.DonationUrl : null,
            DiscordUrl = hasAccess ? plugin.DiscordUrl : null,
            Gallery = hasAccess ? plugin.Gallery : [],
            // 提示语是给访客看的，无权访问的人也需要它（否则解锁框下什么都不写，
            // 访客不知道该去哪拿口令）；「是否已设口令」只对作者有意义。
            AccessHint = plugin.AccessHint,
            HasAccessPassword = !string.IsNullOrEmpty(plugin.AccessPasswordHash),
            CanManage = canManage,
            CanReview = canReview,
            CanManageContent = canManageContent,
            Versions = hasAccess ? versions.Select(v => ToVersion(v, plugin)).ToList() : [],
        };
    }

    /// <summary>
    /// 把扁平的评论列表组装成回复树（按时间正序，兄弟节点稳定排序）。
    /// 孤儿回复（父评论已被删除）不会被丢弃，而是提升为顶层，避免内容凭空消失。
    /// </summary>
    public static List<ScforgeCommentDto> ToCommentTree(
        IReadOnlyList<ScforgeComment> comments,
        string? currentUserId,
        bool isAdmin,
        IReadOnlyDictionary<Guid, int> myVotes)
    {
        var nodes = comments.ToDictionary(
            c => c.Id,
            c =>
            {
                var isAuthor = currentUserId is not null && c.AuthorId == currentUserId;
                return new ScforgeCommentDto
                {
                    Id = c.Id.ToString(),
                    AddonId = c.PluginId.ToString(),
                    ParentId = c.ParentId?.ToString(),
                    Body = c.Body,
                    Author = ToAuthor(c.AuthorId, c.AuthorName, c.AuthorAvatar),
                    CreatedAt = ToBeijing(c.CreatedAt),
                    UpdatedAt = ToBeijing(c.UpdatedAt),
                    Edited = c.UpdatedAt.HasValue,
                    Upvotes = c.Upvotes,
                    Downvotes = c.Downvotes,
                    Score = c.Score,
                    MyVote = myVotes.TryGetValue(c.Id, out var vote) ? vote : 0,
                    // 作者与管理员都可改 / 可删：管理员删除他人评论属于巡查动作。
                    CanEdit = isAuthor || isAdmin,
                    CanDelete = isAuthor || isAdmin,
                };
            });

        var roots = new List<ScforgeCommentDto>();
        foreach (var comment in comments)
        {
            if (comment.ParentId is { } parentId && nodes.TryGetValue(parentId, out var parent))
            {
                parent.Replies.Add(nodes[comment.Id]);
            }
            else
            {
                roots.Add(nodes[comment.Id]);
            }
        }

        return roots;
    }
}
