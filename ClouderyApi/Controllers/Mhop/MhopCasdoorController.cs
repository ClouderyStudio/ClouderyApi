using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using Casdoor.Client;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>
/// Casdoor 统一身份认证登录（OAuth2 授权码 / OIDC）。与用户名密码、邮箱验证码登录并行，
/// 登录成功统一签发 MHOP JWT，前端调用方式与 /mhop/auth/login 完全一致。
/// </summary>
[ApiController]
[Route("mhop/auth/casdoor")]
public class MhopCasdoorController : MhopControllerBase
{
    private static readonly Regex InvalidUsernameChars = new("[^a-zA-Z0-9_\\u4e00-\\u9fa5]", RegexOptions.Compiled);

    private readonly MhopCasdoorService _casdoor;
    private readonly MhopDbContext _db;
    private readonly MhopPasswordHasher _hasher;
    private readonly IMhopJwtService _jwt;
    private readonly ILogger<MhopCasdoorController> _logger;

    public MhopCasdoorController(
        MhopCasdoorService casdoor,
        MhopDbContext db,
        MhopPasswordHasher hasher,
        IMhopJwtService jwt,
        ILogger<MhopCasdoorController> logger)
    {
        _casdoor = casdoor;
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _logger = logger;
    }

    /// <summary>供前端构造 Casdoor 授权地址与跳转回调所需的元数据。</summary>
    [HttpGet("config")]
    public IActionResult GetConfig() => MhopOk(new
    {
        enabled = _casdoor.Enabled,
        endpoint = _casdoor.Endpoint,
        organization_name = _casdoor.OrganizationName,
        application_name = _casdoor.ApplicationName,
        client_id = _casdoor.ClientId,
        scope = _casdoor.Scope,
        redirect_uri = _casdoor.ResolveRedirectUri(Request),
        state_ttl = _casdoor.StateTtl,
    });

    /// <summary>生成一次性的签名 state（防 CSRF 登录），10 分钟内有效。</summary>
    [HttpGet("state")]
    public IActionResult GetState() => MhopOk(new
    {
        state = _casdoor.CreateState(),
        expires_in = _casdoor.StateTtl,
    });

    /// <summary>授权码回调：换取 Casdoor 用户信息 → 绑定/创建本地账号 → 签发 MHOP JWT。</summary>
    [HttpPost("callback")]
    public async Task<IActionResult> Callback([FromBody] CasdoorCallbackIn body)
    {
        if (!_casdoor.Enabled)
            throw new MhopApiException(503, "统一身份认证暂不可用，请联系管理员");
        if (string.IsNullOrWhiteSpace(body.Code))
            throw new MhopApiException(400, "授权码不能为空");
        if (!_casdoor.ValidateState(body.State))
            throw new MhopApiException(400, "state 校验失败，请重新发起登录");

        var redirectUri = string.IsNullOrWhiteSpace(body.RedirectUri)
            ? _casdoor.ResolveRedirectUri(Request)
            : body.RedirectUri.Trim();

        CasdoorUser? casdoorUser;
        try
        {
            casdoorUser = await _casdoor.ExchangeAsync(body.Code, redirectUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Casdoor 统一身份换取令牌失败");
            throw new MhopApiException(502, "统一身份认证换取令牌失败，请重试");
        }

        if (casdoorUser is null || string.IsNullOrEmpty(casdoorUser.Id))
            throw new MhopApiException(502, "获取统一身份用户信息失败");

        var user = await SyncUserAsync(casdoorUser);
        if (user.Status != "active")
            throw new MhopApiException(403, "账号已被停用");

        return MhopOk(TokenOut.Create(_jwt.CreateToken(user.Id, user.Role), user));
    }

    /// <summary>
    /// 同步 Casdoor 用户到本地：优先按 CasdoorId 匹配，其次按邮箱绑定已有账号，最后自动创建。
    /// 本地账号（用户名密码 / 邮箱验证码）不受影响。
    /// </summary>
    private async Task<MhopUser> SyncUserAsync(CasdoorUser casdoorUser)
    {
        var casdoorId = casdoorUser.Id!;
        var email = string.IsNullOrWhiteSpace(casdoorUser.Email)
            ? null
            : casdoorUser.Email.Trim().ToLowerInvariant();

        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.CasdoorId == casdoorId);
        if (user is null && email is not null)
        {
            user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Email == email);
            if (user is not null) user.CasdoorId = casdoorId;
        }

        if (user is null)
        {
            var preferred = SanitizeUsername(casdoorUser.Name)
                            ?? SanitizeUsername(casdoorUser.Email?.Split('@')[0])
                            ?? "user";
            user = new MhopUser
            {
                Username = await GenerateUniqueUsernameAsync(preferred),
                PasswordHash = _hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))),
                Email = email,
                CasdoorId = casdoorId,
                Role = "user",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
            };
            _db.MhopUsers.Add(user);
            _logger.LogInformation("统一身份首次登录，创建 MHOP 账号：{Username}", user.Username);
        }

        if (!string.IsNullOrWhiteSpace(casdoorUser.Avatar)) user.Avatar = casdoorUser.Avatar!;
        await _db.SaveChangesAsync();
        return user;
    }

    private static string? SanitizeUsername(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var cleaned = InvalidUsernameChars.Replace(raw.Trim(), string.Empty);
        if (cleaned.Length == 0) return null;
        return cleaned.Length > 24 ? cleaned[..24] : cleaned;
    }

    private async Task<string> GenerateUniqueUsernameAsync(string preferred)
    {
        if (!await _db.MhopUsers.AnyAsync(u => u.Username == preferred)) return preferred;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var candidate = $"{preferred}_{RandomNumberGenerator.GetInt32(10_000):D4}";
            if (!await _db.MhopUsers.AnyAsync(u => u.Username == candidate)) return candidate;
        }
        return $"{preferred}_{Guid.NewGuid():N}"[..Math.Min(32, preferred.Length + 9)];
    }
}
