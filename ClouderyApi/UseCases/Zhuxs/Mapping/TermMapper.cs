using ClouderyApi.Models.Zhuxs;
using ClouderyApi.Models.Zhuxs.DTOs;

namespace ClouderyApi.UseCases.Zhuxs.Mapping;

/// <summary>
/// Term → 对客输出视图。字段集合与顺序必须与历史“直接返回 Term 实体”的响应一致
/// （实体属性顺序：Id / RecordDate / Description / Information / Files），
/// 嵌套的 TermInfo / TermFile 直接复用实体类型，序列化形状不变。
/// </summary>
internal static class TermMapper
{
    public static TermOut ToOut(Term term) => new()
    {
        Id = term.Id,
        RecordDate = term.RecordDate,
        Description = term.Description,
        Information = term.Information,
        Files = term.Files,
    };
}
