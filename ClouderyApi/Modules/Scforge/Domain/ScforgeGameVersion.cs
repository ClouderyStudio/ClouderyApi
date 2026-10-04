using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// 受支持的游戏版本（生存战争用日期式编号，如 x26.07.01）。
///
/// 为什么放进数据库而不是写死在代码里：版本号每两三周就会往前推一格，
/// 每次都要改代码 + 发版不现实。**超级管理员**可以在后台自行添加，
/// 插件与版本的游戏版本校验都以这张表为准（<see cref="ScforgeCatalog.DefaultGameVersions"/>
/// 只是首次迁移时的初始数据与离线回退）。
/// </summary>
[Table("scforge_game_versions")]
public class ScforgeGameVersion
{
    [Key]
    public Guid Id { get; set; }

    /// <summary>版本号，如 x26.07.01；唯一。</summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>排序权重：越大越新，列表按它倒序。</summary>
    public int SortOrder { get; set; }

    /// <summary>是否仍在内测（未正式发布），前端会标出。</summary>
    public bool Beta { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>添加者显示名（审计用）。</summary>
    public string CreatedBy { get; set; } = string.Empty;
}
