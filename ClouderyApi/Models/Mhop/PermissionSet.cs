using System.Text.Json;

namespace ClouderyApi.Models.Mhop;

/// <summary>
/// 后台模块权限集合（值对象）：与前端后台菜单一一对应的权限码目录，以及权限码数组的
/// 解析 / 过滤 / 序列化规则。superadmin 隐式拥有全部权限且可分配权限；
/// 普通 admin 仅拥有 Permissions 列中显式授予的模块。
/// 存储形态仍是 JSON 字符串列（<c>["dashboard","review"]</c>），空集合序列化为 <c>[]</c>。
/// </summary>
public sealed class PermissionSet
{
    public const string Dashboard = "dashboard"; // 数据看板
    public const string Review = "review";       // 内容审核（帖子/回复/AI 回复管理）
    public const string Users = "users";         // 用户管理
    public const string AiLogs = "ai_logs";      // AI 交互日志
    public const string Bottles = "bottles";     // 漂流瓶审核（瓶子与匿名对话）

    /// <summary>全部合法权限码（顺序即后台菜单顺序）。</summary>
    public static readonly IReadOnlyList<string> All = [Dashboard, Review, Users, AiLogs, Bottles];

    /// <summary>权限码 → 中文名，用于错误提示与后台展示。</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Dashboard] = "数据看板",
        [Review] = "内容审核",
        [Users] = "用户管理",
        [AiLogs] = "AI 交互日志",
        [Bottles] = "漂流瓶审核",
    };

    /// <summary>空集合。</summary>
    public static readonly PermissionSet Empty = new([]);

    private readonly IReadOnlyList<string> _codes;

    private PermissionSet(IReadOnlyList<string> codes) => _codes = codes;

    /// <summary>规范化后的权限码（仅合法码，去重，保持目录顺序以外的输入顺序）。</summary>
    public IReadOnlyList<string> Codes => _codes;

    public bool IsEmpty => _codes.Count == 0;

    public bool Contains(string code) => _codes.Contains(code);

    public string Serialize() => JsonSerializer.Serialize(_codes);

    /// <summary>从任意权限码序列构造：丢弃非法/未知码并去重。</summary>
    public static PermissionSet From(IEnumerable<string>? codes)
        => new(codes is null ? [] : codes.Where(All.Contains).Distinct().ToList());

    /// <summary>解析用户存储的权限码 JSON 数组；空白、非法 JSON 或 null 一律返回空集合。</summary>
    public static PermissionSet Parse(string? json)
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
