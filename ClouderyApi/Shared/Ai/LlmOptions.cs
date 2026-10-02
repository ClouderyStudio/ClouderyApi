namespace ClouderyApi.Shared.Ai;

/// <summary>
/// 通用大模型配置（OpenAI 兼容 Chat Completions）。
/// 读取根级 <c>Llm</c> 配置节；未配置的字段由 Program.cs 回退到既有的 <c>Mhop:Llm</c>，
/// 因此现网只配了 Mhop:Llm 时，新老调用方的行为与本模块抽取前完全一致。
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>接口基址，例如 https://spark-api-open.xf-yun.com/v1 。</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "glm-4-flash";

    /// <summary>单次请求超时（秒），<=0 时按 30 秒处理。</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
