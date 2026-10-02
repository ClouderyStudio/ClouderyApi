using ClouderyApi.Models.Mhop;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 后台模块级权限门面：权限码目录与解析规则已下沉到领域值对象
/// （<see cref="PermissionSet"/>、<see cref="MhopUserRole"/>），这里保留既有调用点使用的别名与判定辅助。
/// superadmin 隐式拥有全部权限且可分配权限；普通 admin 仅拥有 Permissions 字段中显式授予的模块。
/// </summary>
public static class MhopAdminPermissions
{
    public const string Dashboard = PermissionSet.Dashboard; // 数据看板
    public const string Review = PermissionSet.Review;       // 内容审核（帖子/回复/AI 回复管理）
    public const string Users = PermissionSet.Users;         // 用户管理
    public const string AiLogs = PermissionSet.AiLogs;       // AI 交互日志
    public const string Bottles = PermissionSet.Bottles;     // 漂流瓶审核（瓶子与匿名对话）

    public static readonly IReadOnlyList<string> All = PermissionSet.All;

    public static readonly IReadOnlyDictionary<string, string> Labels = PermissionSet.Labels;

    public static bool IsStaff(string? role) => MhopUserRole.IsStaff(role);

    public static bool IsSuper(string? role) => MhopUserRole.IsSuper(role);

    /// <summary>解析用户存储的权限码 JSON 数组，过滤掉非法/未知权限码。</summary>
    public static List<string> Parse(string? json) => [.. PermissionSet.Parse(json).Codes];

    public static string Serialize(IEnumerable<string> codes) => PermissionSet.From(codes).Serialize();

    /// <summary>该用户是否拥有某模块权限（每次请求实时读库，权限调整即时生效）。</summary>
    public static bool Has(MhopUser? user, string code)
    {
        if (user is null || !user.IsStaff) return false;
        if (user.IsSuperAdmin) return true;
        return PermissionSet.Parse(user.Permissions).Contains(code);
    }

    /// <summary>返回用户的有效权限码列表（superadmin 返回全量），用于下发给前端。</summary>
    public static List<string> Effective(MhopUser? user)
    {
        if (user is null || !user.IsStaff) return [];
        return user.IsSuperAdmin ? [.. All] : [.. PermissionSet.Parse(user.Permissions).Codes];
    }
}
