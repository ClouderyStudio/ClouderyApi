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
    public static UserOut ToUserOut(MhopUser user, bool maskPhone = false, bool exposePermissions = true) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        Phone = maskPhone ? null : user.Phone,
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
