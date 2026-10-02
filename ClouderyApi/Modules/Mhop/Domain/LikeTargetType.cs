using ClouderyApi.Shared.Exceptions;

namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>
/// 点赞目标类型（mhop_likes.target_type）：帖子或回复。
/// 只允许这两种取值，非法值在进入仓储前就被拒绝，避免脏数据落到唯一索引上。
/// </summary>
public readonly record struct LikeTargetType
{
    /// <summary>帖子。</summary>
    public const string PostValue = MhopContentKind.Post;

    /// <summary>回复。</summary>
    public const string ReplyValue = MhopContentKind.Reply;

    /// <summary>帖子。</summary>
    public static readonly LikeTargetType Post = new(PostValue);

    /// <summary>回复。</summary>
    public static readonly LikeTargetType Reply = new(ReplyValue);

    private LikeTargetType(string value) => Value = value;

    /// <summary>落库值。</summary>
    public string Value { get; }

    /// <summary>是否为帖子。</summary>
    public bool IsPost => Value == PostValue;

    /// <summary>取值是否合法。</summary>
    public static bool IsValid(string? value) => value is PostValue or ReplyValue;

    /// <summary>尝试解析，失败返回 false 且 <paramref name="target"/> 为默认值。</summary>
    public static bool TryParse(string? value, out LikeTargetType target)
    {
        switch (value)
        {
            case PostValue:
                target = Post;
                return true;
            case ReplyValue:
                target = Reply;
                return true;
            default:
                target = default;
                return false;
        }
    }

    /// <summary>解析点赞目标类型；非法值抛 <see cref="DomainRuleException"/>（文案与历史 400 一致）。</summary>
    public static LikeTargetType Parse(string? value) =>
        TryParse(value, out var target) ? target : throw new DomainRuleException("非法点赞对象");

    /// <inheritdoc />
    public override string ToString() => Value;
}
