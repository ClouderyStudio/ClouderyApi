using System.ComponentModel.DataAnnotations;

namespace ClouderyApi.Models.Zhuxs.DTOs;

/// <summary>
/// 入服申请请求体。POST 时 Passed 恒为 false（由管理员在 PUT 阶段审核），
/// 客户端不可直接通过提交把申请标记为通过（防 over-posting）。
/// </summary>
public class ApplicationDto
{
    public List<Sharable>? Sharables { get; set; }

    /// <summary>审核结果；仅 PUT（管理员）时写入，POST 忽略。</summary>
    public bool? Passed { get; set; }
}
