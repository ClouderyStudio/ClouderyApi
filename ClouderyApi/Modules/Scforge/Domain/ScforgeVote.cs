using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>投票目标类型。</summary>
public static class ScforgeVoteTargets
{
    public const string Plugin = "plugin";
    public const string Comment = "comment";
}

/// <summary>
/// 一条投票（插件与评论统一存放，靠 TargetType 区分）。
///
/// 唯一索引 (UserId, TargetType, TargetId) 保证同一用户对同一目标只有一票，
/// 因此重复投票只会落在更新上；计数列由本表重算，而不是自增，避免并发漂移。
/// </summary>
[Table("scforge_votes")]
public class ScforgeVote
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(64)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(16)]
    public string TargetType { get; set; } = string.Empty;

    public Guid TargetId { get; set; }

    /// <summary>1 = 赞同，-1 = 反对。</summary>
    public int Value { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
