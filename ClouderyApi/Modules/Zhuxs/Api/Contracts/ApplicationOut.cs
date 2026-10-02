using ClouderyApi.Modules.Zhuxs.Domain;

namespace ClouderyApi.Modules.Zhuxs.Api.Contracts;

/// <summary>
/// 入服申请读接口的输出视图。属性声明顺序必须与 Application 实体一致
/// （id → passed → submissionDate → sharables）。
/// </summary>
public class ApplicationOut
{
    public required string Id { get; set; }
    public required bool Passed { get; set; }
    public required DateTime SubmissionDate { get; set; }
    public List<Sharable>? Sharables { get; set; }
}
