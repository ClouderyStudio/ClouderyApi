using ClouderyApi.Models.Zhuxs;
using ClouderyApi.Models.Zhuxs.DTOs;

namespace ClouderyApi.UseCases.Zhuxs.Mapping;

/// <summary>
/// Whitelist → 对客输出视图。字段集合必须与历史“直接返回 Whitelist 实体”的响应一致
/// （实体只有 Id / Code 两个属性）；JSON 顺序由 WhitelistOut 的声明顺序决定。
/// </summary>
internal static class WhitelistMapper
{
    public static WhitelistOut ToOut(Whitelist whitelist) => new()
    {
        Id = whitelist.Id,
        Code = whitelist.Code,
    };
}
