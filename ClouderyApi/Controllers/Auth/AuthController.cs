using Casdoor.Client;
using ClouderyApi.Data;
using ClouderyApi.Models.Qisoul;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Security.Cryptography;

namespace ClouderyApi.Controllers.Auth;

[Route("identity/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private static readonly IConfigurationRoot _config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json")
        .Build();

    private static readonly CasdoorOptions _options = new CasdoorOptions
    {
#pragma warning disable CS8601 // 引用类型赋值可能为 null
        Endpoint = _config["Casdoor:Endpoint"],
        OrganizationName = _config["Casdoor:OrganizationName"],
        ApplicationName = _config["Casdoor:ApplicationName"],
        ApplicationType = _config["Casdoor:ApplicationType"],
        ClientId = _config["Casdoor:ClientId"],
        ClientSecret = _config["Casdoor:ClientSecret"],
        CallbackPath = _config["Casdoor:CallbackPath"],
#pragma warning restore CS8601
    };

    private static readonly CasdoorClient _client = new(new HttpClient(), _options);
    private readonly ILogger<AuthController> _logger;
    private readonly QisoulDbContext _context; // 添加 DbContext

    public AuthController(ILogger<AuthController> logger, QisoulDbContext context)
    {
        _logger = logger;
        _context = context;
    }

    /// <summary>
    /// 生成 OAuth state（防 CSRF 登录）：前端跳转 Casdoor 登录前调用，
    /// 拿到 state 后随登录流程带回，回调时服务端校验与 cookie 一致。
    /// </summary>
    [HttpGet("state")]
    public IActionResult GetOAuthState()
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Response.Cookies.Append("oauth_state", state, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.None,
            Secure = true,
            MaxAge = TimeSpan.FromMinutes(10)
        });

        return Ok(new { success = true, state });
    }

    /// <summary>
    /// OAuth2 回调接口 - 用 code 换取用户信息并建立会话
    /// </summary>
    [HttpPost("callback")]
    public async Task<IActionResult> Callback([FromBody] OAuthCallbackRequest request)
    {
        try
        {
            // 1. 验证请求参数
            if (string.IsNullOrEmpty(request.Code))
            {
                return BadRequest(new { success = false, message = "授权码不能为空" });
            }

            // 1.5 校验 OAuth state（防 CSRF 登录）
            // 登录发起方应先用 GET /identity/auth/state 获取 state，并将该 state 带上，
            // 回调时必须与服务器种下的 cookie 一致。
            var cookieState = Request.Cookies["oauth_state"];
            var hasState = !string.IsNullOrEmpty(request.State);
            if (hasState && (string.IsNullOrEmpty(cookieState) || cookieState != request.State))
            {
                _logger.LogWarning("OAuth state 校验失败，拒绝登录");
                return BadRequest(new { success = false, message = "state 校验失败，请重新发起登录" });
            }
            if (!hasState)
            {
                // 兼容尚未适配 state 的旧前端：放行但记录警告
                _logger.LogWarning("OAuth 回调未携带 state，存在 CSRF 登录风险");
            }

            // 只记录授权码长度，绝不记录明文（授权码为一次性的敏感凭证）
            _logger.LogInformation("收到 OAuth 回调请求，Code 长度: {CodeLength}", request.Code.Length);

            // 2. 用授权码换取 Token
            var tokenResponse = await _client.RequestAuthorizationCodeTokenAsync(
                code: request.Code,
                redirectUri: request.RedirectUri
            );

            if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger.LogWarning("换取 Token 失败");
                return BadRequest(new { success = false, message = "换取访问令牌失败" });
            }

            var accessToken = tokenResponse.AccessToken;
            _logger.LogInformation("成功获取 Access Token");

            // 3. 解析 JWT Token 获取用户信息
            _client.SetBearerToken(accessToken);
            var casdoorUser = _client.ParseJwtToken(accessToken, false);

            if (casdoorUser == null || string.IsNullOrEmpty(casdoorUser.Id))
            {
                _logger.LogWarning("解析用户信息失败");
                return BadRequest(new { success = false, message = "获取用户信息失败" });
            }

            _logger.LogInformation("Casdoor 用户 {UserId} 登录成功", casdoorUser.Id);

            // ===== 新增：同步用户到本地数据库 =====
            var user = await SyncUserToDatabase(casdoorUser);

            if (user == null)
            {
                _logger.LogError("同步用户到数据库失败");
                return StatusCode(500, new { success = false, message = "用户同步失败" });
            }

            // 4. 创建 ClaimsIdentity 并登录（建立 Cookie 会话）
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), // 使用本地数据库的 GUID Id
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                new Claim("Avatar", user.Avatar ?? string.Empty),
                new Claim("CasdoorId", user.CasdoorId),
                new Claim("Provider", "Casdoor"),
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = DateTimeOffset.Now.AddDays(7),
                    AllowRefresh = true,
                }
            );

            _logger.LogInformation("用户 {Username} (ID: {UserId}) 已建立 Cookie 会话", user.Username, user.Id);

            // 5. 返回用户信息
            return Ok(new
            {
                success = true,
                message = "登录成功",
                user = new
                {
                    id = user.Id,
                    username = user.Username,
                    email = user.Email,
                    avatar = user.Avatar,
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理 OAuth 回调时发生异常");
            return StatusCode(500, new { success = false, message = "服务器内部错误" });
        }
    }

    /// <summary>
    /// 同步 Casdoor 用户到本地数据库
    /// </summary>
    private async Task<User?> SyncUserToDatabase(CasdoorUser casdoorUser)
    {
        try
        {
            // 1. 检查用户是否已存在（通过 CasdoorId）
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.CasdoorId == casdoorUser.Id);

            if (existingUser != null)
            {
                // 更新用户信息
                existingUser.Username = casdoorUser.Name ?? casdoorUser.Email?.Split('@')[0] ?? "用户";
                existingUser.Email = casdoorUser.Email;
                existingUser.Avatar = casdoorUser.Avatar;
                existingUser.LastLoginAt = DateTime.Now;

                await _context.SaveChangesAsync();
                _logger.LogInformation("更新用户信息: {UserId}", existingUser.Id);
                return existingUser;
            }

            // 2. 创建新用户
            var newUser = new User
            {
                Id = Guid.NewGuid(),
                Username = casdoorUser.Name ?? casdoorUser.Email?.Split('@')[0] ?? "用户",
                Email = casdoorUser.Email,
                Avatar = casdoorUser.Avatar,
                CasdoorId = casdoorUser.Id ?? string.Empty,
                CreatedAt = DateTime.Now,
                LastLoginAt = DateTime.Now
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            _logger.LogInformation("创建新用户: {UserId}, CasdoorId: {CasdoorId}", newUser.Id, newUser.CasdoorId);
            return newUser;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "同步用户到数据库失败");
            return null;
        }
    }

    /// <summary>
    /// 获取当前登录用户信息
    /// </summary>
    [HttpGet("me")]
    public IActionResult GetCurrentUser()
    {
        try
        {
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return Unauthorized(new { success = false, message = "未登录" });
            }

            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var username = User.FindFirst(ClaimTypes.Name)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;
            var avatar = User.FindFirst("Avatar")?.Value;

            return Ok(new
            {
                success = true,
                user = new
                {
                    id = userId,
                    username = username,
                    email = email,
                    avatar = avatar,
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取当前用户信息失败");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    /// <summary>
    /// 登出接口 - 清除 Cookie 会话
    /// </summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest? request = null)
    {
        try
        {
            var username = User.FindFirst(ClaimTypes.Name)?.Value ?? "未知用户";

            // 清除本地 Cookie 会话
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            _logger.LogInformation("用户 {Username} 已登出", username);

            // 构建响应
            var response = new
            {
                success = true,
                message = "已成功登出",
                // 如果需要跳转到 Casdoor 登出页面，可返回此 URL
                casdoorLogoutUrl = $"{_options.Endpoint}/api/logout"
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "登出时发生异常");
            return StatusCode(500, new { success = false, message = "服务器错误" });
        }
    }

    /// <summary>
    /// 检查登录状态
    /// </summary>
    [HttpGet("status")]
    public IActionResult CheckStatus()
    {
        var isAuthenticated = User.Identity?.IsAuthenticated ?? false;

        if (!isAuthenticated)
        {
            return Ok(new
            {
                success = true,
                isAuthenticated = false,
                message = "未登录"
            });
        }

        return Ok(new
        {
            success = true,
            isAuthenticated = true,
            user = new
            {
                id = User.FindFirst(ClaimTypes.NameIdentifier)?.Value,
                username = User.FindFirst(ClaimTypes.Name)?.Value,
                email = User.FindFirst(ClaimTypes.Email)?.Value,
                avatar = User.FindFirst("Avatar")?.Value,
            }
        });
    }

    /// <summary>
    /// OAuth 回调请求模型
    /// </summary>
    public class OAuthCallbackRequest
    {
        public required string Code { get; set; }
        public string? State { get; set; }
        public required string RedirectUri { get; set; }
    }

    /// <summary>
    /// 登出请求模型（可选）
    /// </summary>
    public class LogoutRequest
    {
        public string? RedirectUri { get; set; }
    }
}