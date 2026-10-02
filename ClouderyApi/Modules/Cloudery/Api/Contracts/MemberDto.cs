using System.ComponentModel.DataAnnotations;
using ClouderyApi.Modules.Cloudery.Domain;

namespace ClouderyApi.Modules.Cloudery.Api.Contracts;

/// <summary>成员请求体：主键由服务端生成，客户端不可覆盖 Id。</summary>
public class MemberDto
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string Position { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }

    public List<Social>? Socials { get; set; }
}
