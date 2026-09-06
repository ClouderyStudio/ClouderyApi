using System.ComponentModel.DataAnnotations;

namespace ClouderyApi.Models.Zhuxs.DTOs;

/// <summary>新增白名单（邀请码）请求体：主键由服务端生成，客户端不可指定。</summary>
public class WhitelistDto
{
    [Required]
    [MaxLength(100)]
    public string Code { get; set; } = string.Empty;
}
