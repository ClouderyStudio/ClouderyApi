using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Mhop;

/// <summary>匿名倾诉论坛主题帖。对应 Python 后端的 posts 表。</summary>
[Table("mhop_posts")]
public class MhopPost
{
    [Key]
    public int Id { get; set; }

    /// <summary>NULL = 纯匿名（历史数据）；匿名仅对前台脱敏，后台仍可追责。</summary>
    public int? UserId { get; set; }

    public bool IsAnonymous { get; set; } = true;

    [Required]
    public string Content { get; set; } = string.Empty;

    /// <summary>板块 slug，见 MhopBoards。</summary>
    [Required]
    [MaxLength(16)]
    public string Board { get; set; } = "mood";

    /// <summary>JSON 数组：帖子附图 URL 列表。</summary>
    public string Images { get; set; } = string.Empty;

    /// <summary>0=待巡检 1=正常 2=违规(隐藏)</summary>
    public int Status { get; set; }

    /// <summary>内容含自伤/自杀信号。</summary>
    public bool Crisis { get; set; }

    public int ViewCount { get; set; }

    [MaxLength(255)]
    public string ReviewNote { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<MhopReply> Replies { get; set; } = new List<MhopReply>();
}
