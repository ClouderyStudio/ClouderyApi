namespace ClouderyApi.Modules.Mhop.Domain;

/// <summary>内容类型：不同场景使用不同的规则与提示词。</summary>
public static class MhopContentKind
{
    /// <summary>论坛主题帖。</summary>
    public const string Post = "post";

    /// <summary>论坛回复。</summary>
    public const string Reply = "reply";

    /// <summary>漂流瓶瓶身（陌生人之间的开场内容）。</summary>
    public const string Bottle = "bottle";

    /// <summary>漂流瓶匿名会话中的单条消息。</summary>
    public const string Message = "message";
}
