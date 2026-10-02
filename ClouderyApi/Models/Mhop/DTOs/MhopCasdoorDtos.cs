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

/// <summary>Casdoor 授权元数据（供前端构造授权地址）。</summary>
public class CasdoorConfigOut
{
    public bool Enabled { get; set; }

    public string? Endpoint { get; set; }

    public string? OrganizationName { get; set; }

    public string? ApplicationName { get; set; }

    public string? ClientId { get; set; }

    public string Scope { get; set; } = string.Empty;

    public string RedirectUri { get; set; } = string.Empty;

    public int StateTtl { get; set; }
}

/// <summary>一次性签名 state（防 CSRF 登录）。</summary>
public class CasdoorStateOut
{
    public string State { get; set; } = string.Empty;

    public int ExpiresIn { get; set; }
}
