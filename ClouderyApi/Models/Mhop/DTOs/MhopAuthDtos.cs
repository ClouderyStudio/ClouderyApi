using System.Text.Json.Serialization;

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

    public string Role { get; set; } = "user";

    public string Status { get; set; } = "active";

    public string Avatar { get; set; } = string.Empty;

    public string Badge { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public static UserOut FromEntity(MhopUser user, bool maskPhone = false) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        Phone = maskPhone ? null : user.Phone,
        Role = user.Role,
        Status = user.Status,
        Avatar = user.Avatar ?? string.Empty,
        Badge = user.Badge ?? string.Empty,
        CreatedAt = user.CreatedAt,
    };
}

public class TokenOut
{
    public string AccessToken { get; set; } = string.Empty;

    public string TokenType { get; set; } = "bearer";

    /// <summary>邮箱验证码首次登录自动注册时为 true。</summary>
    public bool NewAccount { get; set; }

    public UserOut User { get; set; } = new();

    public static TokenOut Create(string accessToken, MhopUser user, bool newAccount = false) => new()
    {
        AccessToken = accessToken,
        NewAccount = newAccount,
        User = UserOut.FromEntity(user),
    };
}
