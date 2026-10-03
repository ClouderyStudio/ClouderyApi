using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Casdoor.Client;
using ClouderyApi.Modules.Mhop.Infrastructure.Persistence;
using ClouderyApi.Shared.Exceptions;
using ClouderyApi.Modules.Mhop.Domain;
using ClouderyApi.Modules.Mhop.Api.Contracts;
using ClouderyApi.Modules.Mhop.Infrastructure;
using ClouderyApi.Modules.Mhop.Application.Mapping;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Mhop.Application;

/// <summary>
/// MHOP 认证用例编排：注册 / 登录 / 邮箱验证码 / 资料与手机号维护 / Casdoor 统一身份。
/// 校验顺序、异常类型（决定 HTTP 状态码）与中文文案均与原控制器逐字一致；
/// 控制器退化为模型绑定 + MhopOk 包装的薄适配器。
/// </summary>
public sealed class AuthAppService
{
    private static readonly Regex PhonePattern = new("^1[3-9]\\d{9}$", RegexOptions.Compiled);
    private static readonly Regex InvalidUsernameChars = new("[^a-zA-Z0-9_\\u4e00-\\u9fa5]", RegexOptions.Compiled);

    private readonly MhopDbContext _db;
    private readonly MhopPasswordHasher _hasher;
    private readonly IMhopJwtService _jwt;
    private readonly MhopEmailCodeService _emailCodes;
    private readonly MhopCurrentUserAccessor _current;
    private readonly ILogger<AuthAppService> _logger;
    private readonly MhopUploadService _uploads;
    private readonly CasdoorAppService _casdoor;

    public AuthAppService(
        MhopDbContext db,
        MhopPasswordHasher hasher,
        IMhopJwtService jwt,
        MhopEmailCodeService emailCodes,
        MhopCurrentUserAccessor current,
        ILogger<AuthAppService> logger,
        MhopUploadService uploads,
        CasdoorAppService casdoor)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
        _emailCodes = emailCodes;
        _current = current;
        _logger = logger;
        _uploads = uploads;
        _casdoor = casdoor;
    }

    /// <summary>注册：长度校验 → 用户名唯一 → 创建用户 → 签发令牌。</summary>
    public async Task<TokenOut> RegisterAsync(RegisterIn body)
    {
        var username = (body.Username ?? string.Empty).Trim();
        if (username.Length < 2 || username.Length > 32)
            throw new DomainRuleException("用户名长度需为 2-32 个字符");
        if (string.IsNullOrEmpty(body.Password) || body.Password.Length < 6 || body.Password.Length > 64)
            throw new DomainRuleException("密码长度需为 6-64 位");
        if (await _db.MhopUsers.AnyAsync(u => u.Username == username))
            throw new DomainRuleException("用户名已存在");

        var user = new MhopUser
        {
            Username = username,
            PasswordHash = _hasher.Hash(body.Password),
            Role = MhopUserRole.User,
            Status = MhopUserStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        _db.MhopUsers.Add(user);
        await _db.SaveChangesAsync();

        return MhopAuthMapper.ToTokenOut(_jwt.CreateToken(user.Id, user.Role), user);
    }

    public async Task<TokenOut> LoginAsync(LoginIn body)
    {
        var username = (body.Username ?? string.Empty).Trim();
        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null || !_hasher.Verify(body.Password ?? string.Empty, user.PasswordHash))
            throw new MhopApiException(401, "用户名或密码错误");
        if (!user.IsActive)
            throw new MhopApiException(403, "账号已被停用");

        return MhopAuthMapper.ToTokenOut(_jwt.CreateToken(user.Id, user.Role), user);
    }

    // ---- 邮箱验证码登录 ----

    public async Task<object> SendEmailCodeAsync(EmailCodeIn body, string ip, CancellationToken cancellationToken)
    {
        var email = (body.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!MhopEmailCodeService.ValidEmail(email))
            throw new DomainRuleException("邮箱格式不正确");

        if (!_emailCodes.CheckIpRate(ip))
            throw new MhopApiException(429, "请求过于频繁，请稍后再试");

        var (code, retryAfter) = _emailCodes.IssueCode(email);
        if (code is null)
            throw new MhopApiException(429, $"发送太频繁，请 {retryAfter} 秒后重试");

        bool delivered;
        try
        {
            delivered = await _emailCodes.DeliverAsync(email, code, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "验证码邮件发送失败：{Email}", email);
            throw new MhopApiException(502, "验证码邮件发送失败，请稍后重试或联系管理员");
        }

        if (!delivered)
        {
            // 仅未配置 SMTP 的开发环境返回验证码，方便本机联调；生产环境不返回
            return new EmailCodeDevOut
            {
                Sent = true,
                Ttl = MhopEmailCodeService.CodeTtlSeconds,
                ResendAfter = MhopEmailCodeService.ResendIntervalSeconds,
                DevMode = true,
                DevCode = code,
            };
        }

        return new EmailCodeOut
        {
            Sent = true,
            Ttl = MhopEmailCodeService.CodeTtlSeconds,
            ResendAfter = MhopEmailCodeService.ResendIntervalSeconds,
        };
    }

    public async Task<TokenOut> LoginByEmailAsync(EmailLoginIn body)
    {
        var email = (body.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!MhopEmailCodeService.ValidEmail(email))
            throw new DomainRuleException("邮箱格式不正确");
        if (!_emailCodes.VerifyCode(email, body.Code ?? string.Empty))
            throw new DomainRuleException("验证码错误或已过期");

        var user = await _db.MhopUsers.FirstOrDefaultAsync(u => u.Email == email);
        var isNew = false;
        if (user is null)
        {
            // 邮箱首次登录：自动注册（随机不可用密码，账号走邮箱登录）
            var baseName = InvalidUsernameChars.Replace(email.Split('@')[0], string.Empty);
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
                Role = MhopUserRole.User,
                Status = MhopUserStatus.Active,
                CreatedAt = DateTime.UtcNow,
            };
            _db.MhopUsers.Add(user);
            await _db.SaveChangesAsync();
            isNew = true;
        }

        if (!user.IsActive)
            throw new MhopApiException(403, "账号已被停用");

        return MhopAuthMapper.ToTokenOut(_jwt.CreateToken(user.Id, user.Role), user, isNew);
    }

    public async Task<UserOut> MeAsync()
    {
        var user = await _current.RequireAsync();
        return MhopAuthMapper.ToUserOut(user);
    }

    public async Task<UserOut> UpdateProfileAsync(ProfileUpdateIn body, CancellationToken cancellationToken)
    {
        var user = await _current.RequireAsync();
        var username = (body.Username ?? string.Empty).Trim();
        if (username.Length < 2)
            throw new DomainRuleException("用户名至少 2 个字符");
        if (await _db.MhopUsers.AnyAsync(u => u.Username == username && u.Id != user.Id))
            throw new DomainRuleException("用户名已被占用");

        user.Username = username;
        var previousAvatar = user.Avatar;
        user.Avatar = body.Avatar ?? string.Empty;
        await _db.SaveChangesAsync();

        // 头像被替换或清空后旧文件不再被引用；外部地址会被存储层自动跳过
        if (!string.Equals(previousAvatar, user.Avatar, StringComparison.Ordinal))
            await _uploads.DeleteAsync([previousAvatar], cancellationToken);

        return MhopAuthMapper.ToUserOut(user);
    }

    public async Task<UserOut> BindPhoneAsync(PhoneBindIn body)
    {
        var user = await _current.RequireAsync();
        var phone = (body.Phone ?? string.Empty).Trim();
        if (!PhonePattern.IsMatch(phone))
            throw new DomainRuleException("手机号格式不正确");
        if (await _db.MhopUsers.AnyAsync(u => u.Phone == phone && u.Id != user.Id))
            throw new DomainRuleException("该手机号已被其他账号绑定");

        user.Phone = phone;
        await _db.SaveChangesAsync();
        return MhopAuthMapper.ToUserOut(user);
    }

    public async Task<UserOut> GetUserPublicAsync(int userId)
    {
        var user = await _db.MhopUsers.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            throw new MhopApiException(404, "用户不存在");
        // 手机号、邮箱与后台权限仅本人与后台可见，公开资料脱敏
        return MhopAuthMapper.ToUserOut(user, maskPhone: true, exposePermissions: false, maskEmail: true);
    }

    // ---- Casdoor 统一身份认证 ----

    /// <summary>供前端构造 Casdoor 授权地址与跳转回调所需的元数据。</summary>
    public CasdoorConfigOut CasdoorConfig(HttpRequest request) => new()
    {
        Enabled = _casdoor.Enabled,
        Endpoint = _casdoor.Endpoint,
        OrganizationName = _casdoor.OrganizationName,
        ApplicationName = _casdoor.ApplicationName,
        ClientId = _casdoor.ClientId,
        Scope = _casdoor.Scope,
        RedirectUri = _casdoor.ResolveRedirectUri(request),
        StateTtl = _casdoor.StateTtl,
    };

    /// <summary>生成一次性的签名 state（防 CSRF 登录），10 分钟内有效。</summary>
    public CasdoorStateOut CasdoorState() => new()
    {
        State = _casdoor.CreateState(),
        ExpiresIn = _casdoor.StateTtl,
    };

    /// <summary>授权码回调：换取 Casdoor 用户信息 → 绑定/创建本地账号 → 签发 MHOP JWT。</summary>
    public async Task<TokenOut> CasdoorCallbackAsync(CasdoorCallbackIn body, HttpRequest request)
    {
        if (!_casdoor.Enabled)
            throw new MhopApiException(503, "统一身份认证暂不可用，请联系管理员");
        if (string.IsNullOrWhiteSpace(body.Code))
            throw new DomainRuleException("授权码不能为空");
        if (!_casdoor.ValidateState(body.State))
            throw new DomainRuleException("state 校验失败，请重新发起登录");

        var redirectUri = string.IsNullOrWhiteSpace(body.RedirectUri)
            ? _casdoor.ResolveRedirectUri(request)
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
        if (!user.IsActive)
            throw new MhopApiException(403, "账号已被停用");

        return MhopAuthMapper.ToTokenOut(_jwt.CreateToken(user.Id, user.Role), user);
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
                            ?? MhopUserRole.User;
            user = new MhopUser
            {
                Username = await GenerateUniqueUsernameAsync(preferred),
                PasswordHash = _hasher.Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))),
                Email = email,
                CasdoorId = casdoorId,
                Role = MhopUserRole.User,
                Status = MhopUserStatus.Active,
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
