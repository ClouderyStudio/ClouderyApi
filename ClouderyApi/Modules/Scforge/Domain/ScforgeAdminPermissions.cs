using System.Text.Json;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// 管理员角色。
///
/// - <c>super</c>：超级管理员。拥有全部权限，并且是**唯一**能授予 / 调整 / 撤销管理员的人。
/// - <c>admin</c>：普通管理员。由超管指定，只拥有 <see cref="ScforgePermissionSet"/> 中显式授予的权限。
/// </summary>
public static class ScforgeAdminRoles
{
    public const string Super = "super";
    public const string Admin = "admin";

    public static bool IsSuper(string? role) => string.Equals(role, Super, StringComparison.Ordinal);

    public static bool IsValid(string? role) =>
        string.Equals(role, Super, StringComparison.Ordinal) || string.Equals(role, Admin, StringComparison.Ordinal);
}

/// <summary>
/// 管理员模块权限集合（值对象）。
///
/// 沿用 MHOP <c>PermissionSet</c> 的形态：权限码数组以 JSON 字符串列存储，
/// 解析时丢弃未知码；超管隐式拥有全部权限，因此这里只描述「普通管理员」被显式授予的部分。
/// </summary>
public sealed class ScforgePermissionSet
{
    /// <summary>审核权限：处理待审核的插件与版本（通过 / 驳回）。</summary>
    public const string Review = "review";

    /// <summary>内容管理权限：编辑任意插件的资料与版本、删除插件。</summary>
    public const string Content = "content";

    /// <summary>全部可授予权限（顺序即后台面板上的展示顺序）。</summary>
    public static readonly IReadOnlyList<string> All = [Review, Content];

    /// <summary>权限码 → 中文名，用于后台勾选与错误提示。</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Review] = "审核权限",
        [Content] = "内容管理",
    };

    /// <summary>权限码 → 一句话说明。</summary>
    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [Review] = "处理待审核的插件提交与版本文件，可通过或驳回并附理由",
        [Content] = "编辑任意插件的资料与版本、删除插件（不参与审核流程）",
    };

    public static readonly ScforgePermissionSet Empty = new([]);

    private readonly IReadOnlyList<string> _codes;

    private ScforgePermissionSet(IReadOnlyList<string> codes) => _codes = codes;

    public IReadOnlyList<string> Codes => _codes;

    public bool IsEmpty => _codes.Count == 0;

    public bool Contains(string code) => _codes.Contains(code, StringComparer.Ordinal);

    public string Serialize() => JsonSerializer.Serialize(_codes);

    /// <summary>从任意权限码序列构造：丢弃未知码并去重。</summary>
    public static ScforgePermissionSet From(IEnumerable<string>? codes) =>
        new(codes is null ? [] : codes.Where(All.Contains).Distinct(StringComparer.Ordinal).ToList());

    /// <summary>解析存储的权限码 JSON 数组；空白、非法 JSON 或 null 一律返回空集合。</summary>
    public static ScforgePermissionSet Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Empty;
        try
        {
            var codes = JsonSerializer.Deserialize<List<string>>(json);
            return codes is null ? Empty : From(codes);
        }
        catch
        {
            return Empty;
        }
    }
}
