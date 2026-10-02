using Microsoft.Extensions.Configuration;

namespace ClouderyApi.Shared.Options;

/// <summary>
/// SCKEY 后端转发配置（appsettings.json 的 <c>Env</c> 节）。
/// 键名保持历史取值 <c>Env:SCKEY_API_BASE</c> / <c>Env:SCKEY_BEARER_TOKEN</c>。
/// <para>
/// 旧代码先读 <c>Env:*</c>、再回退 <c>SurvivalCraft:*</c>、最后回退内置默认值；
/// 该回退在 Program.cs 的 PostConfigure 中实现，保留 <c>??</c> 语义：
/// 只有“键缺失”（null）才回退，显式空串仍是有效取值。
/// </para>
/// </summary>
public sealed class SckeyOptions
{
    public const string SectionName = "Env";

    /// <summary>SCKEY 后端基地址。缺失时回退 <c>SurvivalCraft:SCKEY_API_BASE</c> 或内置默认值。</summary>
    [ConfigurationKeyName("SCKEY_API_BASE")]
    public string? ApiBase { get; set; }

    /// <summary>SCKEY Bearer 令牌。缺失时回退 <c>SurvivalCraft:SCKEY_BEARER_TOKEN</c> 或空串。</summary>
    [ConfigurationKeyName("SCKEY_BEARER_TOKEN")]
    public string? BearerToken { get; set; }
}
