using ClouderyApi.Modules.Cloudery.Domain;

namespace ClouderyApi.Modules.Cloudery.Api.Contracts;

/// <summary>
/// 成员读接口的输出视图。属性声明顺序即 JSON 键顺序，必须与历史上
/// “直接返回 Member 实体” 的响应逐字一致（id → name → position → description → socials）。
/// </summary>
public class MemberOut
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Position { get; set; }
    public string? Description { get; set; }
    public List<Social>? Socials { get; set; }
}
