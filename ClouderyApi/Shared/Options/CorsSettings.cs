namespace ClouderyApi.Shared.Options;

/// <summary>
/// 跨域白名单配置（appsettings.json 的 <c>Cors</c> 节）。
/// <see cref="AllowedOrigins"/> 缺失时保持 null，调用方按既有逻辑回退为空列表。
/// </summary>
public sealed class CorsSettings
{
    public const string SectionName = "Cors";

    /// <summary>允许的跨域来源（大小写不敏感比较由调用方完成）。</summary>
    public string[]? AllowedOrigins { get; set; }
}
