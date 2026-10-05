namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// SCForge 平台的目录常量与校验规则。
///
/// 平台的分类、标签、发布渠道与游戏版本都在这里收敛：前端
/// <c>src/data/catalog.ts</c> 只是这些值的展示副本，服务端是权威来源，
/// 因此非法取值会被直接拒绝，而不是静默落库。
/// </summary>
public static class ScforgeCatalog
{
    /// <summary>插件分类键（与前端分类 chip 一一对应）。</summary>
    public static readonly IReadOnlyDictionary<string, string> Categories = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["gameplay"] = "玩法扩展",
        ["utilities"] = "实用工具",
        ["world"] = "世界生成",
        ["mobs"] = "实体与生物",
        ["storage"] = "存储与物品",
        ["economy"] = "经济与商店",
        ["protection"] = "防护与安全",
        ["performance"] = "性能优化",
        ["api"] = "开发库 / API",
        ["integration"] = "集成桥接",
        ["misc"] = "其它",
    };

    /// <summary>插件标签键。</summary>
    public static readonly IReadOnlyDictionary<string, string> Tags = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["survival"] = "生存",
        ["creative"] = "创造",
        ["pvp"] = "PvP",
        ["pve"] = "PvE",
        ["multiplayer"] = "多人",
        ["singleplayer"] = "单人",
        ["adventure"] = "冒险",
        ["technical"] = "技术",
        ["decoration"] = "装饰",
        ["magic"] = "魔法",
        ["technology"] = "科技",
        ["food"] = "食物",
        ["transport"] = "交通",
        ["mining"] = "采矿",
        ["farming"] = "农业",
        ["server"] = "服务器",
        ["client"] = "客户端",
        ["library"] = "前置库",
        ["chinese"] = "中文支持",
        ["open-source"] = "开源",
    };

    /// <summary>资源类型：插件只在服务端加载，模组会随服务器下发到客户端。</summary>
    public const string PluginKind = "plugin";
    public const string ModKind = "mod";

    /// <summary>插件包扩展名。</summary>
    public const string PluginPackageExtension = ".dll";

    /// <summary>模组包扩展名。</summary>
    public const string ModPackageExtension = ".netmod";

    /// <summary>两种资源类型的键与中文名（顺序即板块顺序：插件在前）。</summary>
    public static readonly IReadOnlyDictionary<string, string> Kinds = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [PluginKind] = "插件",
        [ModKind] = "模组",
    };

    public static bool IsKind(string? kind) => !string.IsNullOrEmpty(kind) && Kinds.ContainsKey(kind);

    /// <summary>该资源类型要求的包扩展名。</summary>
    public static string PackageExtensionFor(string kind) => kind == ModKind ? ModPackageExtension : PluginPackageExtension;

    /// <summary>发布渠道：正式版 / 测试版 / 预览版。</summary>
    public static readonly IReadOnlyList<string> Channels = ["release", "beta", "alpha"];

    /// <summary>
    /// 初始游戏版本（新的在前）：只在首次迁移时写入数据库，之后以
    /// <see cref="ScforgeGameVersion"/> 表为准（超管可在后台继续添加）。
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultGameVersions =
    [
        "x26.07.01", "x26.06.19", "x26.05.23",
    ];

    /// <summary>
    /// 版本号格式：x + 两位年 + 若干两位段，如 x26.07.01（允许 x26.07.01.1 之类的补丁后缀）。
    /// 只做格式校验，具体是否受支持看数据库里的那张表。
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex GameVersionPattern =
        new(@"^x\d{2}(\.\d{1,2})+$", System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>排序键。</summary>
    public static readonly IReadOnlyList<string> SortKeys = ["relevance", "downloads", "recent", "name"];

    public const int MaxTags = 6;
    public const int MaxGallery = 6;
    public const int MaxNameLength = 80;
    public const int MaxSummaryLength = 280;
    public const int MaxSlugLength = 64;
    public const int MaxVersionLength = 40;
    public const int MaxCommentLength = 4000;
    public const int MaxDescriptionLength = 20_000;
    public const int MaxReadmeLength = 60_000;
    public const int MaxDependencies = 20;
    public const int MaxGameVersionsPerRelease = 10;

    /// <summary>白名单模式下单个插件最多授权多少人。</summary>
    public const int MaxAccessGrants = 200;

    /// <summary>访问说明（口令模式下的提示语）长度上限。</summary>
    public const int MaxAccessHintLength = 200;

    /// <summary>分类键是否合法。</summary>
    public static bool IsCategory(string? key) => key is not null && Categories.ContainsKey(key);

    /// <summary>发布渠道是否合法。</summary>
    public static bool IsChannel(string? key) => key is not null && Channels.Contains(key, StringComparer.Ordinal);

    /// <summary>游戏版本是否受支持。</summary>
    /// <summary>版本号是否形如 x26.07.01。</summary>
    public static bool IsGameVersionFormat(string? value) =>
        !string.IsNullOrWhiteSpace(value) && GameVersionPattern.IsMatch(value.Trim());

    /// <summary>把 slug 归一化为小写字母 / 数字 / 连字符。</summary>
    public static string NormalizeSlug(string? raw)
    {
        var source = (raw ?? string.Empty).Trim().ToLowerInvariant();
        var buffer = new System.Text.StringBuilder(source.Length);
        var lastDash = false;
        foreach (var ch in source)
        {
            if (char.IsAsciiLetterOrDigit(ch))
            {
                buffer.Append(ch);
                lastDash = false;
            }
            else if (!lastDash && buffer.Length > 0)
            {
                buffer.Append('-');
                lastDash = true;
            }
        }

        var slug = buffer.ToString().TrimEnd('-');
        return slug.Length > MaxSlugLength ? slug[..MaxSlugLength].TrimEnd('-') : slug;
    }
}
