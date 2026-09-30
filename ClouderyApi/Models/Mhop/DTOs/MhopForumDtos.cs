using System.Text.Json.Serialization;

namespace ClouderyApi.Models.Mhop.DTOs;

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
