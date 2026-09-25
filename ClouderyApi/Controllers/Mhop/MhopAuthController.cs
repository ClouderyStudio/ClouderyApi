using System.Security.Cryptography;
using System.Text.RegularExpressions;
using ClouderyApi.Data;
using ClouderyApi.Models.Mhop;
using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Services.Mhop;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>注册 / 登录 / 当前用户。匿名访问不需要任何令牌（对应 Python 后端 routers/auth.py）。</summary>
[ApiController]
[Route("mhop/auth")]
public class MhopAuthController : MhopControllerBase
{
    private static readonly Regex PhonePattern = new("^1[3-9]\\d{9}$", RegexOptions.Compiled);
    private static readonly Regex UsernamePattern = new("[^a-zA-Z0-9_\\u4e00-\\u9fa5]", RegexOptions.Compiled);

    private readonly MhopDbContext _db;
    private readonly MhopPasswordHasher _hasher;
    private readonly IMhopJwtService _jwt;
    private readonly MhopEmailCodeService _emailCodes;
    private readonly MhopCurrentUserAccessor _current;
    private readonly ILogger<MhopAuthController> _logger;
    private readonly MhopUploadService _uploads;

    public MhopAuthController(
        MhopDbContext db,
        MhopPasswordHasher hasher,
        IMhopJwtService jwt,
        MhopEmailCodeService emailCodes,
        MhopCurrentUserAccessor current,
        ILogger<MhopAuthController> logger,
        MhopUploadService uploads)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _emailCodes = emailCodes;
        _current = current;
        _logger = logger;
        _uploads = uploads;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterIn body)
    {
        var username = (body.Username ?? string.Empty).Trim();
        if (username.Length < 2 || username.Length > 32)
            throw new MhopApiException(400, "用户名长度需为 2-32 个字符");
        if (string.IsNullOrEmpty(body.Password) || body.Password.Length < 6 || body.Password.Length > 64)
            throw new MhopApiException(400, "密码长度需为 6-64 位");
        if (await _db.MhopUsers.AnyAsync(u => u.Username == username))
            throw new MhopApiException(400, "用户名已存在");

        var user = new MhopUser
        {
            Username = username,
            PasswordHash = _hasher.Hash(body.Password),
            Role = "user",
            Status = "active",
            CreatedAt = DateTime.UtcNow,
        };
        _db.MhopUsers.Add(user);
        await _db.SaveChangesAsync();

        return MhopOk(TokenOut.Create(_jwt.CreateToken(user.Id, user.Role), user));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginIn body)
    {
        var username = (body.Username ?? string.Empty).Trim();
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null || !_hasher.Verify(body.Password ?? string.Empty, user.PasswordHash))
            throw new MhopApiException(401, "用户名或密码错误");
        if (user.Status != "active")
            throw new MhopApiException(403, "账号已被停用");

        return MhopOk(TokenOut.Create(_jwt.CreateToken(user.Id, user.Role), user));
    }

    // ---- 邮箱验证码登录 ----

    [HttpPost("email-code")]
    public async Task<IActionResult> SendEmailCode([FromBody] EmailCodeIn body)
    {
        var email = (body.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!MhopEmailCodeService.ValidEmail(email))
            throw new MhopApiException(400, "邮箱格式不正确");

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!_emailCodes.CheckIpRate(ip))
            throw new MhopApiException(429, "请求过于频繁，请稍后再试");

        var (code, retryAfter) = _emailCodes.IssueCode(email);
        if (code is null)
            throw new MhopApiException(429, $"发送太频繁，请 {retryAfter} 秒后重试");

        bool delivered;
        try
        {
            delivered = await _emailCodes.DeliverAsync(email, code, HttpContext.RequestAborted);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证码邮件发送失败：{Email}", email);
            throw new MhopApiException(502, "验证码邮件发送失败，请稍后重试或联系管理员");
        }

        if (!delivered)
        {
            // 仅未配置 SMTP 的开发环境返回验证码，方便本机联调；生产环境不返回
            return MhopOk(new
            {
                sent = true,
                ttl = MhopEmailCodeService.CodeTtlSeconds,
                resend_after = MhopEmailCodeService.ResendIntervalSeconds,
                dev_mode = true,
                dev_code = code,
            });
        }

        return MhopOk(new
        {
            sent = true,
            ttl = MhopEmailCodeService.CodeTtlSeconds,
            resend_after = MhopEmailCodeService.ResendIntervalSeconds,
        });
    }

    [HttpPost("login-email")]
    public async Task<IActionResult> LoginByEmail([FromBody] EmailLoginIn body)
    {
        var email = (body.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!MhopEmailCodeService.ValidEmail(email))
            throw new MhopApiException(400, "邮箱格式不正确");
        if (!_emailCodes.VerifyCode(email, body.Code ?? string.Empty))
            throw new MhopApiException(400, "验证码错误或已过期");

        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Email == email);
        var isNew = false;
        if (user is null)
        {
            // 邮箱首次登录：自动注册（随机不可用密码，账号走邮箱登录）
            var baseName = UsernamePattern.Replace(email.Split('@')[0], string.Empty);
            if (baseName.Length == 0) baseName = "user";
            if (baseName.Length > 24) baseName = baseName[..24];

            string? username = null;
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var candidate = $"{baseName}_{RandomNumberGenerator.GetInt32(10_000):D4}";
                if (!await _db.MhopUsers.AnyAsync(u => u.Username == candidate))
                {
                    username = candidate;
                    break;
                }
            }
            if (username is null)
                throw new MhopApiException(500, "注册失败，请稍后重试");

            user = new MhopUser
            {
                Username = username,
                PasswordHash = _hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))),
                Email = email,
                Role = "user",
                Status = "active",
                CreatedAt = DateTime.UtcNow,
            };
            _db.MhopUsers.Add(user);
            await _db.SaveChangesAsync();
            isNew = true;
        }

        if (user.Status != "active")
            throw new MhopApiException(403, "账号已被停用");

        return MhopOk(TokenOut.Create(_jwt.CreateToken(user.Id, user.Role), user, isNew));
    }

    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        var user = await _current.RequireAsync();
        return MhopOk(UserOut.FromEntity(user));
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] ProfileUpdateIn body)
    {
        var user = await _current.RequireAsync();
        var username = (body.Username ?? string.Empty).Trim();
        if (username.Length < 2)
            throw new MhopApiException(400, "用户名至少 2 个字符");
        if (await _db.MhopUsers.AnyAsync(u => u.Username == username && u.Id != user.Id))
            throw new MhopApiException(400, "用户名已被占用");

        user.Username = username;
        var previousAvatar = user.Avatar;
        user.Avatar = body.Avatar ?? string.Empty;
        await _db.SaveChangesAsync();

        // 头像被替换或清空后旧文件不再被引用；外部地址会被存储层自动跳过
        if (!string.Equals(previousAvatar, user.Avatar, StringComparison.Ordinal))
            await _uploads.DeleteAsync([previousAvatar], HttpContext.RequestAborted);

        return MhopOk(UserOut.FromEntity(user));
    }

    [HttpPut("me/phone")]
    public async Task<IActionResult> BindPhone([FromBody] PhoneBindIn body)
    {
        var user = await _current.RequireAsync();
        var phone = (body.Phone ?? string.Empty).Trim();
        if (!PhonePattern.IsMatch(phone))
            throw new MhopApiException(400, "手机号格式不正确");
        if (await _db.MhopUsers.AnyAsync(u => u.Phone == phone && u.Id != user.Id))
            throw new MhopApiException(400, "该手机号已被其他账号绑定");

        user.Phone = phone;
        await _db.SaveChangesAsync();
        return MhopOk(UserOut.FromEntity(user));
    }

    [HttpGet("users/{userId:int}")]
    public async Task<IActionResult> GetUserPublic(int userId)
    {
        var user = await _db.MhopUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            throw new MhopApiException(404, "用户不存在");
        // 手机号仅本人与后台可见，公开资料脱敏
        return MhopOk(UserOut.FromEntity(user, maskPhone: true));
    }
}
