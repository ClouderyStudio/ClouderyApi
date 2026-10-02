namespace ClouderyApi.Shared.Ai;

/// <summary>
/// 大模型调用入口。实现必须「失败不抛异常」：返回空文本 + 引擎标识 local，
/// 由调用方决定本地兜底文案（这是 MHOP 原有的降级约定）。
/// </summary>
public interface ILlmClient
{
    /// <summary>是否配置了可用的模型端点（BaseUrl + ApiKey 都非空）。</summary>
    bool IsConfigured { get; }

    /// <summary>调用 OpenAI 兼容接口；返回 (回复文本, 引擎标识 llm/local)。</summary>
    Task<(string Text, string Engine)> ChatAsync(
        IReadOnlyList<Dictionary<string, string>> messages,
        CancellationToken cancellationToken = default,
        double temperature = 0.7,
        int maxTokens = 700);
}
