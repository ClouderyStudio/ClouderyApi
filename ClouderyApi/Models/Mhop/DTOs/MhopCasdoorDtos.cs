using System.Text.Json.Serialization;

namespace ClouderyApi.Models.Mhop.DTOs;

/// <summary>Casdoor 授权码回调请求体。</summary>
public class CasdoorCallbackIn
{
    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;

    [JsonPropertyName("state")] public string State { get; set; } = string.Empty;

    /// <summary>必须与发起授权时使用的 redirect_uri 完全一致。</summary>
    [JsonPropertyName("redirect_uri")] public string RedirectUri { get; set; } = string.Empty;
}
