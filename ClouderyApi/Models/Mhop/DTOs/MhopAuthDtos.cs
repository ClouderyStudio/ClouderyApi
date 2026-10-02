using System.Text.Json.Serialization;
using ClouderyApi.Services.Mhop;
using ClouderyApi.Modules.Mhop.Application.Mapping;
using ClouderyApi.Modules.Mhop.Domain;

namespace ClouderyApi.Models.Mhop.DTOs;

// 请求 DTO：MVC 请求体绑定使用默认 camelCase 策略，因此用 [JsonPropertyName] 显式声明蛇形键名，
// 与前端 axios 发送的字段（is_anonymous / target_type 等）保持一致。

public class RegisterIn
{
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
}

public class LoginIn
{
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;

    [JsonPropertyName("password")] public string Password { get; set; } = string.Empty;
}

public class EmailCodeIn
{
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;
}

public class EmailLoginIn
{
    [JsonPropertyName("email")] public string Email { get; set; } = string.Empty;

    [JsonPropertyName("code")] public string Code { get; set; } = string.Empty;
}

public class PhoneBindIn
{
    [JsonPropertyName("phone")] public string Phone { get; set; } = string.Empty;
}

public class ProfileUpdateIn
{
    [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;

    [JsonPropertyName("avatar")] public string Avatar { get; set; } = string.Empty;
}

/// <summary>响应 DTO：经 MhopJson 的蛇形策略序列化。</summary>
public class UserOut
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string Role { get; set; } = MhopUserRole.User;

    public string Status { get; set; } = MhopUserStatus.Active;

    public string Avatar { get; set; } = string.Empty;

    public string Badge { get; set; } = string.Empty;

    /// <summary>有效权限码列表（superadmin 为全量），用于前端菜单/按钮/路由控制；后端接口另有强校验。</summary>
    public List<string> Permissions { get; set; } = [];

    public DateTime CreatedAt { get; set; }

}

public class TokenOut
{
    public string AccessToken { get; set; } = string.Empty;

    public string TokenType { get; set; } = "bearer";

    /// <summary>邮箱验证码首次登录自动注册时为 true。</summary>
    public bool NewAccount { get; set; }

    public UserOut User { get; set; } = new();
}

/// <summary>邮箱验证码发送结果（已实际投递）。</summary>
public class EmailCodeOut
{
    public bool Sent { get; set; }

    public int Ttl { get; set; }

    public int ResendAfter { get; set; }
}

/// <summary>邮箱验证码发送结果（未配置 SMTP 的开发环境，额外返回 dev_mode / dev_code）。</summary>
public class EmailCodeDevOut
{
    public bool Sent { get; set; }

    public int Ttl { get; set; }

    public int ResendAfter { get; set; }

    public bool DevMode { get; set; }

    public string DevCode { get; set; } = string.Empty;
}
