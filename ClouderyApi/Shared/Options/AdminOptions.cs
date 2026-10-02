namespace ClouderyApi.Shared.Options;

/// <summary>
/// 管理员白名单配置（appsettings.json 的 <c>Authorization</c> 节）。
/// <see cref="Admins"/> 缺失时保持 null，调用方按既有逻辑回退为空列表。
/// </summary>
public sealed class AdminOptions
{
    public const string SectionName = "Authorization";

    /// <summary>允许执行管理操作的 CasdoorId 列表（大小写不敏感比较由调用方完成）。</summary>
    public string[]? Admins { get; set; }
}
