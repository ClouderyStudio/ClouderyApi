using ClouderyApi.Modules.Zhuxs.Domain;

namespace ClouderyApi.Modules.Zhuxs.Api.Contracts;

/// <summary>
/// 赛季信息读接口的输出视图。属性声明顺序必须与 Term 实体一致
/// （id → recordDate → description → information → files）。
/// </summary>
public class TermOut
{
    public required string Id { get; set; }
    public required string RecordDate { get; set; }
    public required string Description { get; set; }
    public required TermInfo Information { get; set; }
    public List<TermFile>? Files { get; set; }
}
