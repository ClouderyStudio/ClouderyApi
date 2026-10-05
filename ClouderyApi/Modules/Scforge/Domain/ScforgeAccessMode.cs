namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// 资源的访问模式：控制「谁能看到、能下载」。
///
/// 隐私判定是**整包**的（不做到版本级），语义三条：
///   <list type="bullet">
///     <item><see cref="Public"/>：完全公开，与引入隐私前行为一致，也是默认值。</item>
///     <item><see cref="Password"/>：知道口令即可访问。适合小范围分享（测试服、服主群）。
///       口令只存 PBKDF2 哈希，**任何接口都取不回明文**，包括作者与超管。</item>
///     <item><see cref="Whitelist"/>：只有作者在名单里列出的用户可以访问。适合内部插件、
///       未发布的商业插件这类「知道有，但不给外人看」的场景。</item>
///   </list>
///
/// 隐私插件**不进公开目录**（搜索 / 精选 / 最近 / 分类计数都看不到），
/// 但仍可通过 slug 直达详情页 —— 否则作者没法把链接分享给别人。
/// 作者本人与 SCForge 管理员在任何模式下都放行（管理员要审核它）。
/// </summary>
public static class ScforgeAccessMode
{
    /// <summary>公开：任何人都能看与下载。</summary>
    public const string Public = "public";

    /// <summary>口令访问：需先提交口令换取解锁令牌。</summary>
    public const string Password = "password";

    /// <summary>白名单访问：调用者 userId 需在名单内。</summary>
    public const string Whitelist = "whitelist";

    /// <summary>模式目录（下发给前端，前端不硬编码）。</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Public] = "公开",
        [Password] = "口令访问",
        [Whitelist] = "指定人员可见",
    };

    public static readonly IReadOnlyList<string> All = [Public, Password, Whitelist];

    public static bool IsValid(string? mode) => !string.IsNullOrEmpty(mode) && Labels.ContainsKey(mode);

    /// <summary>归一化：缺省按公开处理，非法取值直接拒绝（不静默落库）。</summary>
    public static string Normalize(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return Public;

        var normalized = mode.Trim().ToLowerInvariant();
        if (!IsValid(normalized)) throw new ScforgeRuleException("访问方式只能是 public / password / whitelist");
        return normalized;
    }

    /// <summary>口令的最小长度：低于这个值一律拒绝，不做「短口令也能用」的妥协。</summary>
    public const int MinPasswordLength = 6;

    /// <summary>口令的最大长度：给 PBKDF2 的输入封顶，避免超长输入拖慢哈希（DoS 面）。</summary>
    public const int MaxPasswordLength = 128;
}
