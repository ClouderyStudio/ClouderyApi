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

    public async Task<MhopUser> RequireAdminAsync()
    {
        var user = await RequireAsync();
        if (user.Role != "admin") throw new MhopApiException(403, "需要管理员权限");
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
