using ClouderyApi.Models.Cloudery;
using ClouderyApi.Models.Cloudery.DTOs;

namespace ClouderyApi.UseCases.Cloudery.Mapping;

/// <summary>
/// Member / Social → 对客输出视图。属性赋值顺序不影响 JSON（顺序由 DTO 声明顺序决定），
/// 但字段集合必须与历史“直接返回 Member 实体”的响应一致。
/// </summary>
internal static class MemberMapper
{
    public static MemberOut ToOut(Member member) => new()
    {
        Id = member.Id,
        Name = member.Name,
        Position = member.Position,
        Description = member.Description,
        Socials = member.Socials,
    };
}
