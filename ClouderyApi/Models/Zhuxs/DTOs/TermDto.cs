using System.ComponentModel.DataAnnotations;

namespace ClouderyApi.Models.Zhuxs.DTOs;

/// <summary>赛季信息请求体：主键由服务端生成。</summary>
public class TermDto
{
    [Required]
    [MaxLength(100)]
    public string RecordDate { get; set; } = string.Empty;

    [Required]
    [MaxLength(1000)]
    public string Description { get; set; } = string.Empty;

    public TermInfo Information { get; set; } = new TermInfo
    {
        Name = string.Empty,
        From = string.Empty,
        Version = string.Empty,
        Modcount = 0,
        Playercount = 0
    };


    public List<TermFile>? Files { get; set; }
}
