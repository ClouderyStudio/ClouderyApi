using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Api.Contracts;
using ClouderyApi.Modules.Mhop.Infrastructure;

namespace ClouderyApi.Modules.Mhop.Application.Mapping;

/// <summary>
/// MHOP 认证相关的实体 → 输出 DTO 纯映射。赋值顺序与 <see cref="UserOut"/> / <see cref="TokenOut"/>
/// 的声明顺序一致，保证经 MhopJson 蛇形序列化后的字段顺序与重构前逐字对齐。
/// </summary>
public static class MhopAuthMapper
{
    /// <summary>
    /// <paramref name="maskPhone"/> / <paramref name="maskEmail"/> 用于公开资料脱敏：
    /// 手机号与邮箱都是登录凭据（邮箱验证码登录按邮箱查用户），公开接口不得下发，
    /// 否则可按 userId 遍历全站 PII。本人 / 后台接口用默认值即完整下发。
    /// </summary>
    public static UserOut ToUserOut(
        MhopUser user,
        bool maskPhone = false,
        bool exposePermissions = true,
        bool maskEmail = false) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = maskEmail ? null : user.Email,
        Phone = maskPhone ? null : user.Phone,
        EmailVerified = user.EmailVerifiedAt is not null,
        Role = user.Role,
        Status = user.Status,
        Avatar = user.Avatar ?? string.Empty,
        Badge = user.Badge ?? string.Empty,
        // 对外公开资料不下发后台权限，避免泄露管理能力
        Permissions = exposePermissions ? MhopAdminPermissions.Effective(user) : [],
        CreatedAt = user.CreatedAt,
    };

    public static TokenOut ToTokenOut(string accessToken, MhopUser user, bool newAccount = false) => new()
    {
        AccessToken = accessToken,
        NewAccount = newAccount,
        User = ToUserOut(user),
    };
}
