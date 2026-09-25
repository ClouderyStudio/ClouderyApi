using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 当前登录用户解析，对应 Python 后端的 deps.py（可选鉴权 / 登录用户 / 管理员 / 已绑定手机号）。
/// 结果按请求缓存在 HttpContext.Items，避免同一请求内重复查库。
/// </summary>
public sealed class MhopCurrentUserAccessor
{
    private const string CacheKey = "Mhop.CurrentUser";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly MhopDbContext _db;
    private readonly IMhopJwtService _jwt;

    public MhopCurrentUserAccessor(IHttpContextAccessor httpContextAccessor, MhopDbContext db, IMhopJwtService jwt)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _jwt = jwt;
    }

    public async Task<MhopUser?> GetOptionalAsync()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context is null) return null;
        if (context.Items.TryGetValue(CacheKey, out var cached)) return cached as MhopUser;

        var user = await ResolveAsync(context);
        context.Items[CacheKey] = user;
        return user;
    }

    public async Task<MhopUser> RequireAsync()
        => await GetOptionalAsync() ?? throw new MhopApiException(401, "请先登录");

    /// <summary>任意后台人员：普通管理员或超级管理员（模块级权限请用 RequirePermAsync）。</summary>
    public async Task<MhopUser> RequireAdminAsync()
    {
        var user = await RequireAsync();
        if (!MhopAdminPermissions.IsStaff(user.Role)) throw new MhopApiException(403, "需要管理员权限");
        return user;
    }

    /// <summary>超级管理员：角色管理、权限分配等专属操作。</summary>
    public async Task<MhopUser> RequireSuperAsync()
    {
        var user = await RequireAdminAsync();
        if (!MhopAdminPermissions.IsSuper(user.Role))
            throw new MhopApiException(403, "仅超级管理员可执行该操作");
        return user;
    }

    /// <summary>后台人员且拥有指定模块权限；传入多个权限码时任一满足即可。</summary>
    public async Task<MhopUser> RequirePermAsync(params string[] perms)
    {
        var user = await RequireAdminAsync();
        if (perms.Length > 0 && !perms.Any(p => MhopAdminPermissions.Has(user, p)))
        {
            var label = perms.Length == 1
                ? MhopAdminPermissions.Labels.GetValueOrDefault(perms[0], perms[0])
                : string.Join(" / ", perms.Select(p => MhopAdminPermissions.Labels.GetValueOrDefault(p, p)));
            throw new MhopApiException(403, $"没有「{label}」模块权限");
        }
        return user;
    }

    public async Task<MhopUser> RequirePhoneVerifiedAsync()
    {
        var user = await RequireAsync();
        if (string.IsNullOrEmpty(user.Phone)) throw new MhopApiException(403, "发帖前请先在个人主页绑定手机号");
        return user;
    }

    private async Task<MhopUser?> ResolveAsync(HttpContext context)
    {
        var header = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return null;

        var payload = _jwt.ValidateToken(header["Bearer ".Length..].Trim());
        if (payload is null) return null;

        // 注意：这里必须返回被跟踪的实体——BindPhone / UpdateProfile 等接口会直接修改
        // 该实例并调用 SaveChangesAsync；若用 AsNoTracking 会导致更新静默丢失。
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Id == payload.UserId);
        if (user is null || user.Status != "active") return null;
        return user;
    }
}
