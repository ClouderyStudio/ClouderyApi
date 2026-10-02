using System.Text.Json.Serialization;

namespace ClouderyApi.Modules.Mhop.Api.Contracts;

public class PostIn
{
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;

    [JsonPropertyName("is_anonymous")] public bool IsAnonymous { get; set; } = true;

    [JsonPropertyName("board")] public string Board { get; set; } = string.Empty;

    [JsonPropertyName("images")] public List<string> Images { get; set; } = [];
}

public class ReplyIn
{
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;

    [JsonPropertyName("is_anonymous")] public bool IsAnonymous { get; set; } = true;

    [JsonPropertyName("images")] public List<string> Images { get; set; } = [];
}

public class LikeIn
{
    [JsonPropertyName("target_type")] public string TargetType { get; set; } = string.Empty;

    [JsonPropertyName("target_id")] public int TargetId { get; set; }
}

public class ReplyOut
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public string Content { get; set; } = string.Empty;

    public int Status { get; set; }

    public bool IsAi { get; set; }

    public bool IsAnonymous { get; set; }

    public string Author { get; set; } = string.Empty;

    public string AuthorAvatar { get; set; } = string.Empty;

    public string AuthorBadge { get; set; } = string.Empty;

    public bool Crisis { get; set; }

    public bool Recalled { get; set; }

    public string RecallReason { get; set; } = string.Empty;

    public int LikeCount { get; set; }

    public bool Liked { get; set; }

    public List<string> Images { get; set; } = [];

    public DateTime CreatedAt { get; set; }
}

public class PostOut
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;

    public string Board { get; set; } = "mood";

    public int Status { get; set; }

    public bool Crisis { get; set; }

    public bool IsAnonymous { get; set; }

    public string Author { get; set; } = string.Empty;

    public string AuthorAvatar { get; set; } = string.Empty;

    public string AuthorBadge { get; set; } = string.Empty;

    public int ReplyCount { get; set; }

    public int ViewCount { get; set; }

    public bool AiReplied { get; set; }

    public bool Mine { get; set; }

    public int LikeCount { get; set; }

    public bool Liked { get; set; }

    public List<string> Images { get; set; } = [];

    public DateTime? LastReplyAt { get; set; }

    public string LastReplyAuthor { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}

public class PostDetailOut : PostOut
{
    public List<ReplyOut> Replies { get; set; } = [];
}

public class PostListOut
{
    public int Total { get; set; }

    public int Page { get; set; }

    public int Size { get; set; }

    public List<PostOut> Items { get; set; } = [];
}

// ---- Stage 2 应用层输出 DTO：论坛用例（ForumAppService）----

/// <summary>板块列表项。</summary>
public class BoardOut
{
    public string Slug { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public string Desc { get; set; } = string.Empty;

    public int Count { get; set; }
}

/// <summary>论坛公开统计。</summary>
public class ForumStatsOut
{
    public int Posts { get; set; }

    public int Replies { get; set; }

    public int Users { get; set; }

    public int Online { get; set; }
}

/// <summary>点赞切换结果。</summary>
public class LikeToggleOut
{
    public bool Liked { get; set; }

    public int LikeCount { get; set; }
}

/// <summary>我的点赞目标 ID 列表。</summary>
public class LikeIdsOut
{
    public List<int> Ids { get; set; } = [];
}

/// <summary>按状态汇总的计数。</summary>
public class MineCountOut
{
    public int Total { get; set; }

    public int Pending { get; set; }

    public int Published { get; set; }

    public int Rejected { get; set; }

    public int Draft { get; set; }
}

/// <summary>个人主页统计：帖子 / 回复各自按状态汇总。</summary>
public class MineSummaryOut
{
    public MineCountOut Posts { get; set; } = new();

    public MineCountOut Replies { get; set; } = new();
}

/// <summary>我的帖子列表项（含审核字段）。</summary>
public class MyPostOut
{
    public int Id { get; set; }

    public string Content { get; set; } = string.Empty;

    public string Board { get; set; } = string.Empty;

    public int Status { get; set; }

    public bool Crisis { get; set; }

    public bool IsAnonymous { get; set; }

    public List<string> Images { get; set; } = [];

    public int ReplyCount { get; set; }

    public int ViewCount { get; set; }

    public int LikeCount { get; set; }

    public string ReviewNote { get; set; } = string.Empty;

    public string AiFlag { get; set; } = string.Empty;

    public string AiReviewNote { get; set; } = string.Empty;

    public bool Editable { get; set; }

    public bool CanWithdraw { get; set; }

    public bool CanSubmit { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>我的帖子分页列表。</summary>
public class MyPostListOut
{
    public int Total { get; set; }

    public int Page { get; set; }

    public int Size { get; set; }

    public List<MyPostOut> Items { get; set; } = [];
}

/// <summary>我的回复列表项（含父帖摘要与审核字段）。</summary>
public class MyReplyOut
{
    public int Id { get; set; }

    public int PostId { get; set; }

    public string PostExcerpt { get; set; } = string.Empty;

    public int? PostStatus { get; set; }

    public string Content { get; set; } = string.Empty;

    public int Status { get; set; }

    public bool Crisis { get; set; }

    public bool IsAnonymous { get; set; }

    public List<string> Images { get; set; } = [];

    public int LikeCount { get; set; }

    public string ReviewNote { get; set; } = string.Empty;

    public string AiFlag { get; set; } = string.Empty;

    public string AiReviewNote { get; set; } = string.Empty;

    public bool Editable { get; set; }

    public bool CanWithdraw { get; set; }

    public bool CanSubmit { get; set; }

    public DateTime CreatedAt { get; set; }
}

/// <summary>我的回复分页列表。</summary>
public class MyReplyListOut
{
    public int Total { get; set; }

    public int Page { get; set; }

    public int Size { get; set; }

    public List<MyReplyOut> Items { get; set; } = [];
}

/// <summary>帖子编辑结果（含危机标记）。</summary>
public class PostUpdateOut
{
    public bool Ok { get; set; } = true;

    public int Status { get; set; }

    public bool Crisis { get; set; }
}

/// <summary>内容状态流转结果。</summary>
public class ContentStatusOut
{
    public bool Ok { get; set; } = true;

    public int Status { get; set; }
}

/// <summary>仅返回 ok 的操作结果。</summary>
public class OkOut
{
    public bool Ok { get; set; } = true;
}

