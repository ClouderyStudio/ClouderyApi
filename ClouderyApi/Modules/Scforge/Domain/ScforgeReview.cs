namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// 插件与版本的审核状态。
///
/// 平台采用「先审后发」：新提交、作者编辑过的内容、作者替换过的版本文件都会回到
/// <see cref="Pending"/>，只有管理员（拥有 review 权限）通过后才会对公众可见。
/// </summary>
public static class ScforgeContentStatus
{
    /// <summary>待审核：仅作者与管理员可见。</summary>
    public const int Pending = 1;

    /// <summary>已通过：对公众可见。</summary>
    public const int Published = 2;

    /// <summary>已驳回：仅作者与管理员可见，作者可修改后重新提交。</summary>
    public const int Rejected = 3;

    public static readonly IReadOnlyList<int> All = [Pending, Published, Rejected];

    /// <summary>状态的对外小写标识（下发给前端）。</summary>
    public static string ToKey(int status) => status switch
    {
        Pending => "pending",
        Published => "published",
        Rejected => "rejected",
        _ => "pending",
    };

    /// <summary>把前端/查询串里的状态标识解析成数值；无法识别时返回 null。</summary>
    public static int? Parse(string? key) => key?.Trim().ToLowerInvariant() switch
    {
        "pending" => Pending,
        "published" => Published,
        "rejected" => Rejected,
        _ => null,
    };

    /// <summary>状态的中文名，用于后台与作者视图。</summary>
    public static string Label(int status) => status switch
    {
        Pending => "待审核",
        Published => "已发布",
        Rejected => "已驳回",
        _ => "未知",
    };
}
