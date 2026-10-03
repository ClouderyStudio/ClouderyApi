using ClouderyApi.Modules.Mhop.Api.Contracts;
using ClouderyApi.Modules.Mhop.Application;
using ClouderyApi.Shared.Filters;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Mhop.Api;

/// <summary>注册 / 登录 / 当前用户。匿名访问不需要任何令牌（对应 Python 后端 routers/auth.py）。</summary>
[ApiController]
[Route("mhop/auth")]
public class MhopAuthController : MhopControllerBase
{
    private readonly AuthAppService _auth;

    public MhopAuthController(AuthAppService auth)
    {
        _auth = auth;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterIn body)
        => MhopOk(await _auth.RegisterAsync(body));

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginIn body)
        => MhopOk(await _auth.LoginAsync(body));

    // ---- 邮箱验证码登录 ----

    [HttpPost("email-code")]
    public async Task<IActionResult> SendEmailCode([FromBody] EmailCodeIn body)
        => MhopOk(await _auth.SendEmailCodeAsync(
            body,
            ClientIp.Resolve(HttpContext),
            HttpContext.RequestAborted));

    [HttpPost("login-email")]
    public async Task<IActionResult> LoginByEmail([FromBody] EmailLoginIn body)
        => MhopOk(await _auth.LoginByEmailAsync(body));

    [HttpGet("me")]
    public async Task<IActionResult> Me()
        => MhopOk(await _auth.MeAsync());

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] ProfileUpdateIn body)
        => MhopOk(await _auth.UpdateProfileAsync(body, HttpContext.RequestAborted));

    [HttpPut("me/phone")]
    public async Task<IActionResult> BindPhone([FromBody] PhoneBindIn body)
        => MhopOk(await _auth.BindPhoneAsync(body));

    [HttpGet("users/{userId:int}")]
    public async Task<IActionResult> GetUserPublic(int userId)
        => MhopOk(await _auth.GetUserPublicAsync(userId));
}
