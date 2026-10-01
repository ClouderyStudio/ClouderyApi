namespace ClouderyApi.Services.Mhop;

/// <summary>
/// AI 内容审核结论。Verdict 取值见常量；Reason 为给人工审核人员的理由（通过时为空）。
/// </summary>
public sealed record MhopModerationOutcome(string Verdict, string Reason)
{
    /// <summary>通过：可自动公开。</summary>
    public const string Safe = "safe";

    /// <summary>疑似风险：转人工复核。</summary>
    public const string Suspect = "suspect";

    /// <summary>明确违规：转人工复核（不自动公开）。</summary>
    public const string Violation = "violation";

    /// <summary>AI 未配置 / 调用失败，无法判断：一律转人工，绝不自动公开。</summary>
    public const string Unavailable = "unavailable";

    public bool IsSafe => Verdict == Safe;
}
