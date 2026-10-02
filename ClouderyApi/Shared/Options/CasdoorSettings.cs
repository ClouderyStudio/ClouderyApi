namespace ClouderyApi.Shared.Options;

/// <summary>
/// Casdoor 统一身份认证配置（appsettings.json 的 <c>Casdoor</c> 节）。
/// 属性名与既有配置直读的键名一一对应，语义完全一致：
/// 引用类型默认 null 表示键缺失，由调用方按既有逻辑回退。
/// </summary>
public sealed class CasdoorSettings
{
    public const string SectionName = "Casdoor";

    public string? Endpoint { get; set; }

    public string? OrganizationName { get; set; }

    public string? ApplicationName { get; set; }

    public string? ApplicationType { get; set; }

    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public string? CallbackPath { get; set; }

    /// <summary>OIDC 授权范围；留空或空数组时调用方回退为 "openid profile email"。</summary>
    public string[]? Scopes { get; set; }
}
