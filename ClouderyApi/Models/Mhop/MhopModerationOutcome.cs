namespace ClouderyApi.Models.Mhop;

/// <summary>
/// AI 内容审核结论。Verdict 取值见常量（帖子 / 回复 / 瓶身 / 会话消息共用同一口径）；
/// Reason 为给人工审核人员的理由（通过时为空）。
/// </summary>
public sealed record MhopModerationOutcome(string Verdict, string Reason)
{
    /// <summary>未审核 / 无标记（数据库默认值）。</summary>
    public const string None = "";

    /// <summary>通过：可自动公开。</summary>
    public const string Safe = "safe";

    /// <summary>疑似风险：转人工复核。</summary>
    public const string Suspect = "suspect";

    /// <summary>明确违规：转人工复核（不自动公开 / 消息立即隐藏）。</summary>
    public const string Violation = "violation";

    /// <summary>AI 未配置 / 调用失败，无法判断：一律转人工，绝不自动公开。</summary>
    public const string Unavailable = "unavailable";

    /// <summary>人工放行：写入后 AI 重跑不再翻案，只更新理由留痕。</summary>
    public const string Approved = "approved";

    public bool IsSafe => Verdict == Safe;
}
