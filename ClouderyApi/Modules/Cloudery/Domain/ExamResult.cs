using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ClouderyApi.Modules.Cloudery.Domain;

/// <summary>
/// 心理学站点（psychology）用户保存在云端的一条测评结果。
///
/// 结果正文以 JSON 原样存放在 Payload 里：站点本机存档的对象结构（维度、画像、
/// AI 解读、备注等）会随版本不断变化，拆成固定列不但每次都要迁移，还会丢掉站点新加的字段。
/// 需要按量排序/去重的少数几个字段单独成列。
/// </summary>
public class ExamResult
{
    [Key]
    [MaxLength(36)]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>本地 User.Id（Casdoor 登录后同步的用户）；跨设备共享按此归属，不做跨库外键</summary>
    public Guid UserId { get; set; }

    [Required]
    [MaxLength(64)]
    public string TestId { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? TestTitle { get; set; }

    /// <summary>站点本机记录键（如 test_phq9_result_1759...），用于幂等同步：同一台设备的同一条记录重复上传不会产生副本</summary>
    [MaxLength(120)]
    public string? ClientKey { get; set; }

    /// <summary>结果在站点侧写入本机存档的时间，跨设备排序以它为准（客户端时钟可能不准，仅用于排序）</summary>
    public DateTime? SavedAt { get; set; }

    [Column(TypeName = "longtext")]
    public string Payload { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>服务端最后一次写入时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
