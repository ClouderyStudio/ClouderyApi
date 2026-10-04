namespace ClouderyApi.Modules.Scforge.Api.Contracts;

/*
 * API Key 管理的输入 / 输出模型。
 *
 * 输出刻意**不含**令牌明文：只有创建（Create）响应里额外带一个 token 字段，
 * 之后再任何接口都取不回来（含超管）。界面上只显示 prefix 与掩码。
 */

/// <summary>签发一把 API Key 的表单（application/json 即可，不必 multipart）。</summary>
public class ScforgeApiKeyCreateForm
{
    /// <summary>这把 Key 的名字，例：「发版机器人」。留空按「未命名」处理。</summary>
    public string? Name { get; set; }

    /// <summary>作用域键；可从 <c>GET /scforge/api-keys/scopes</c> 取合法值。至少一个。</summary>
    public List<string>? Scopes { get; set; }

    /// <summary>过期时间（UTC）。留空 = 长期有效。</summary>
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>吊销一把 Key。</summary>
public class ScforgeApiKeyRevokeForm
{
    /// <summary>吊销原因，写入审计记录，便于日后追溯。</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Key 列表项。
///
/// 只暴露前缀与末尾 4 位，完整令牌永不出库 —— 前端展示用。
/// </summary>
public class ScforgeApiKeyOut
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>令牌前缀，例：<c>scf_a1b2</c>。</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>给界面看的掩码令牌，例：<c>scf_a1b2…wxyz</c>。</summary>
    public string MaskedToken { get; set; } = string.Empty;

    public IReadOnlyList<string> Scopes { get; set; } = [];

    /// <summary>作用域的中文名列表，界面直接显示用。</summary>
    public IReadOnlyList<string> ScopeLabels { get; set; } = [];

    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;

    /// <summary>时间一律输出北京时间（UTC+8），与 SCForge 其余 DTO 一致（见 <c>ScforgeMapper.ToBeijing</c>）。</summary>
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public string? RevokedReason { get; set; }
    public DateTimeOffset? LastUsedAt { get; set; }
    public string? LastUsedIp { get; set; }

    /// <summary>有效 / 已吊销 / 已过期。</summary>
    public string Status { get; set; } = string.Empty;

    public bool Usable { get; set; }
}
