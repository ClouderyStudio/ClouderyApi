using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// 一名管理员（含超管）。
///
/// 身份键是 Identity 域 <c>Users.Id</c>（即会话 ClaimTypes.NameIdentifier），
/// 用户名为写入时快照，避免后台列表跨库联表。
/// 首次引导：<c>Authorization:Admins</c>（CasdoorId 白名单）里的账号即使没有本表记录，
/// 也按超级管理员处理 —— 这样第一个超管不必手工插入数据库就能授权他人。
/// </summary>
[Table("scforge_admins")]
public class ScforgeAdmin
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Identity 域用户 Id（唯一）。</summary>
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(80)]
    public string UserName { get; set; } = string.Empty;

    [MaxLength(512)]
    public string? Avatar { get; set; }

    /// <summary>见 <see cref="ScforgeAdminRoles"/>。</summary>
    [Required]
    [MaxLength(16)]
    public string Role { get; set; } = ScforgeAdminRoles.Admin;

    /// <summary>权限码 JSON 数组；超管恒为全量，这里仍保存以便展示。</summary>
    public string Permissions { get; set; } = "[]";

    /// <summary>授予人（超管）的用户名快照。</summary>
    [MaxLength(80)]
    public string? GrantedBy { get; set; }

    /// <summary>配置白名单引导出来的超管标记：仅用于后台展示「来源」。</summary>
    public bool FromConfig { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public bool IsSuper => ScforgeAdminRoles.IsSuper(Role);

    /// <summary>该管理员的有效权限码（超管为全量）。</summary>
    public IReadOnlyList<string> EffectivePermissions =>
        IsSuper ? ScforgePermissionSet.All : ScforgePermissionSet.Parse(Permissions).Codes;
}
