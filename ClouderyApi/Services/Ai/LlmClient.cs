using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Services.Ai;

/// <summary>
/// OpenAI 兼容 Chat Completions 客户端：实现整体来自 MhopAiService.ChatAsync（逐行等价搬迁）。
/// 约定（对外可见，勿改）：
/// 1) 返回的引擎标识字符串是 "llm" / "local"，其中 "llm" 会写入 MhopAiLog.Engine 并在管理后台展示；
/// 2) 未配置端点或调用失败时只记警告、返回 (string.Empty, "local")，绝不抛异常——调用方据此走本地兜底。
/// </summary>
public sealed class LlmClient : ILlmClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmOptions _options;
    private readonly ILogger<LlmClient> _logger;

    public LlmClient(
        IHttpClientFactory httpClientFactory,
        IOptions<LlmOptions> options,
        ILogger<LlmClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsConfigured
        => !string.IsNullOrWhiteSpace(_options.BaseUrl) && !string.IsNullOrWhiteSpace(_options.ApiKey);

    /// <inheritdoc />
    public async Task<(string Text, string Engine)> ChatAsync(
        IReadOnlyList<Dictionary<string, string>> messages,
        CancellationToken cancellationToken = default,
        double temperature = 0.7,
        int maxTokens = 700)
    {
        if (IsConfigured)
        {
            try
            {
                var timeoutSeconds = _options.TimeoutSeconds > 0 ? _options.TimeoutSeconds : 30;
                var client = _httpClientFactory.CreateClient();
                using var request = new HttpRequestMessage(
                    HttpMethod.Post, _options.BaseUrl.TrimEnd('/') + "/chat/completions");
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _options.ApiKey);
                request.Content = new StringContent(
                    JsonSerializer.Serialize(new
                    {
                        model = _options.Model,
                        messages,
                        temperature,
                        max_tokens = maxTokens,
                    }),
                    Encoding.UTF8,
                    "application/json");

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

                using var response = await client.SendAsync(request, cts.Token);
                response.EnsureSuccessStatusCode();

                var payload = await response.Content.ReadAsStringAsync(cts.Token);
                using var document = JsonDocument.Parse(payload);
                var text = document.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString();

                if (!string.IsNullOrWhiteSpace(text))
                {
                    return (text.Trim(), "llm");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM 调用失败，降级为本地引擎");
            }
        }

        return (string.Empty, "local");
    }
}
