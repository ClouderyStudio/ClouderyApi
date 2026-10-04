using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// 一把 SCForge API Key：给命令行 / CI 用的机器凭据，让脚本能代替作者发布插件与版本。
///
/// 设计要点：
///   • **只存哈希，不存明文**（<see cref="KeyHash"/> = SHA-256(base64url(token))）。
///     明文只在创建响应里出现一次，之后任何接口都取不回来 —— 包括超管。
///   • 令牌形如 <c>scf_&lt;base64url(32 字节)&gt;</c>，前缀让日志与流量里一眼能认出并便于封禁扫描。
///   • <b>作用域</b>（<see cref="Scopes"/>）按 JSON 数组存，最小权限：只给 <c>publish</c>
///     就只能传包，不能列表 / 吊销。
///   • 归属人（<see cref="UserId"/>）决定用这把 Key 发布的作品归谁，与浏览器登录上传完全一致；
///     归属校验继续由 <see cref="Application.ScforgeActor"/> 承担。
///   • 吊销是软状态（<see cref="RevokedAt"/>），历史记录保留便于审计「谁在什么时候用过」。
/// </summary>
[Table("scforge_api_keys")]
public class ScforgeApiKey
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>令牌前缀（不含随机段），用于界面展示与快速识别，例如 <c>scf_a1b2</c>。</summary>
    [Required]
    [MaxLength(16)]
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// 令牌的 SHA-256 哈希（base64url）。**不是**密码学意义上的密码 —— 令牌随机度足够高，
    /// 离线爆破不可行，所以不需要 bcrypt/argon 这类慢哈希。
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string KeyHash { get; set; } = string.Empty;

    /// <summary>归属用户 Id（Identity 域 <c>Users.Id</c>）。用这把 Key 发布的作品归他名下。</summary>
    public Guid UserId { get; set; }

    /// <summary>归属用户名快照，避免后台列表跨库联表。</summary>
    [Required]
    [MaxLength(80)]
    public string UserName { get; set; } = string.Empty;

    /// <summary>人可读的名字，例：「发版机器人」「我的电脑」。</summary>
    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    /// <summary>作用域 JSON 数组（见 <see cref="ScforgeApiKeyScopes"/>）。空数组按「无任何权限」处理。</summary>
    public string Scopes { get; set; } = "[]";

    /// <summary>过期时间；<c>null</c> 表示长期有效。</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>吊销时间；非 <c>null</c> 即已失效。</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>吊销原因（超管填写，便于审计）。</summary>
    [MaxLength(200)]
    public string? RevokedReason { get; set; }

    /// <summary>创建者 CasdoorId 快照：区分「本人自助创建」与「超管代发」。</summary>
    [MaxLength(100)]
    public string? CreatedByCasdoorId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最后一次被用来调 API 的时间（不用于鉴权，仅供后台展示活跃度）。</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>最后一次使用的客户端 IP 快照，便于识别异常调用来源。</summary>
    [MaxLength(45)]
    public string? LastUsedIp { get; set; }
}
