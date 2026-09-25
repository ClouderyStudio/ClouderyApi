using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Models.Mhop;

/// <summary>心理量表评估记录（仅登录用户显式勾选保存到云端时落库）。</summary>
[Table("mhop_assessments")]
public class MhopAssessment
{
    [Key]
    public int Id { get; set; }

    public int? UserId { get; set; }

    /// <summary>phq9 / gad7 / free</summary>
    [Required]
    [MaxLength(32)]
    public string AssessmentType { get; set; } = string.Empty;

    /// <summary>JSON 序列化的作答。</summary>
    public string InputData { get; set; } = string.Empty;

    public string AiResult { get; set; } = string.Empty;

    public int? Score { get; set; }

    [MaxLength(32)]
    public string Level { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
