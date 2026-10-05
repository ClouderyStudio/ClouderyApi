using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// 隐私插件的授权名单条目：白名单模式下，谁可以访问这个插件。
///
/// 与插件是 1:N（<see cref="PluginId"/> 外键，级联删除）：插件被删，名单一起消失，
/// 不留悬空授权。作者名做快照是为了让插件被删前的审计记录仍可读。
///
/// 名单只对 <b>whitelist</b> 模式有意义；切到 public / password 时应用层会清空它
/// （不靠数据库约束 —— 留着无害，但留着会让人误以为切回白名单时授权还在）。
/// </summary>
[Table("scforge_access_grants")]
public class ScforgeAccessGrant
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>所属插件 Id。</summary>
    public Guid PluginId { get; set; }

    /// <summary>被授权用户 Id（Identity 域 <c>Users.Id</c> 的字符串形式，与 <c>ScforgePlugin.AuthorId</c> 同口径）。</summary>
    [Required]
    [MaxLength(64)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>用户名快照：避免后台展示时跨库联表。</summary>
    [Required]
    [MaxLength(80)]
    public string UserName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
