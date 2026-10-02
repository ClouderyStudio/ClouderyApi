namespace ClouderyApi.Models.Mhop;

/// <summary>
/// 帖子 / 回复正文：构造即校验（trim → 非空 → 长度上限），把原来散落在各 action 里的校验收敛到一处。
/// </summary>
public readonly record struct ContentText
{
    /// <summary>帖子正文上限。</summary>
    public const int MaxPostLength = 2000;

    /// <summary>回复正文上限。</summary>
    public const int MaxReplyLength = 1000;

    public string Value { get; }

    private ContentText(string value) => Value = value;

    /// <summary>帖子正文：1–2000 字。</summary>
    public static ContentText ForPost(string? raw) => Create(raw, MaxPostLength, "内容");

    /// <summary>回复正文：1–1000 字。</summary>
    public static ContentText ForReply(string? raw) => Create(raw, MaxReplyLength, "回复内容");

    public bool IsEmpty => Value.Length == 0;

    private static ContentText Create(string? raw, int maxLength, string label)
    {
        var value = (raw ?? string.Empty).Trim();
        if (value.Length == 0) throw new DomainRuleException($"{label}不能为空");
        if (value.Length > maxLength) throw new DomainRuleException($"{label}不能超过 {maxLength} 字");
        return new ContentText(value);
    }

    public override string ToString() => Value;
}

/// <summary>论坛板块 slug：构造即校验必须属于 <see cref="MhopBoards.Slugs"/>。</summary>
public readonly record struct BoardSlug
{
    public string Value { get; }

    private BoardSlug(string value) => Value = value;

    /// <summary>校验板块 slug；注意与历史行为一致，不做 trim。</summary>
    public static BoardSlug Create(string? raw)
    {
        var value = raw ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value) || !MhopBoards.Slugs.Contains(value))
            throw new DomainRuleException("请选择板块");
        return new BoardSlug(value);
    }

    public override string ToString() => Value;
}

/// <summary>
/// 内容安全初筛结果：危机信号 + 命中敏感词。
/// 由调用方在边界处用 MhopModeration 计算后交给领域方法，领域层不反向依赖服务层。
/// </summary>
public readonly record struct ContentScreening(bool Crisis, IReadOnlyCollection<string> SensitiveWords)
{
    public bool HasSensitiveWords => SensitiveWords.Count > 0;
}

/// <summary>内容审核相关的共享约束与工具。</summary>
public static class ContentRules
{
    /// <summary>审核理由列宽（mhop_posts.review_note / mhop_replies.review_note）。</summary>
    public const int ReviewNoteMaxLength = 255;

    /// <summary>审核理由入库前的截断。</summary>
    public static string TruncateReviewNote(string? note)
    {
        var value = note ?? string.Empty;
        return value.Length <= ReviewNoteMaxLength ? value : value[..ReviewNoteMaxLength];
    }
}
