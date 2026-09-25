using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Mhop;

/// <summary>
/// MHOP 平台用户（论坛 / 心理评估 / 管理后台共用）。对应 Python 后端的 users 表。
/// 表名加 mhop_ 前缀，避免与栖所（Qisoul）等已有域的 Users 表冲突。
/// </summary>
[Table("mhop_users")]
public class MhopUser
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(64)]
    public string Username { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>邮箱验证码登录；可为空（存量为 NULL）。</summary>
    [MaxLength(255)]
    public string? Email { get; set; }

    /// <summary>手机号；发帖前必须绑定（不做短信验证）。</summary>
    [MaxLength(20)]
    public string? Phone { get; set; }

    /// <summary>Casdoor 统一身份认证用户 ID（OAuth2/OIDC 登录时绑定；本地账号为 NULL）。</summary>
    [MaxLength(100)]
    public string? CasdoorId { get; set; }

    /// <summary>admin / superadmin / user</summary>
    [Required]
    [MaxLength(16)]
    public string Role { get; set; } = "user";

    /// <summary>
    /// 普通管理员被授予的后台模块权限码 JSON 数组，如 ["dashboard","review"]；
    /// 仅 role=admin 有意义，superadmin 隐式拥有全部权限。空字符串表示未授权。
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string Permissions { get; set; } = string.Empty;

    /// <summary>active / disabled</summary>
    [Required]
    [MaxLength(16)]
    public string Status { get; set; } = "active";

    /// <summary>头像 URL 路径</summary>
    [Required]
    [MaxLength(255)]
    public string Avatar { get; set; } = string.Empty;

    /// <summary>管理员设置的用户标识（如「认证咨询师」「志愿者」）</summary>
    [Required]
    [MaxLength(64)]
    public string Badge { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
