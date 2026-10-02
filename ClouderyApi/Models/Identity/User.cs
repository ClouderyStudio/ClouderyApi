using System.ComponentModel.DataAnnotations;

namespace ClouderyApi.Models.Identity;

/// <summary>
/// 本地登录用户：Casdoor 回调时按 CasdoorId 增量同步，
/// 供身份会话与 /exam/results 等按本地用户 Id 归属的数据使用。
/// 表名沿用历史 Users 表，迁移到独立身份域后表结构与数据保持不变。
/// </summary>
public class User
{
    [Key]
    public Guid Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Email { get; set; }

    [MaxLength(500)]
    public string? Avatar { get; set; }

    [Required]
    [MaxLength(100)]
    public string CasdoorId { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? LastLoginAt { get; set; }
}
