using ClouderyApi.Models.Mhop.DTOs;
using ClouderyApi.Modules.Mhop.Application;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Controllers.Mhop;

/// <summary>
/// Casdoor 统一身份认证登录（OAuth2 授权码 / OIDC）。与用户名密码、邮箱验证码登录并行，
/// 登录成功统一签发 MHOP JWT，前端调用方式与 /mhop/auth/login 完全一致。
/// </summary>
[ApiController]
[Route("mhop/auth/casdoor")]
public class MhopCasdoorController : MhopControllerBase
{
    private readonly AuthAppService _auth;

    public MhopCasdoorController(AuthAppService auth)
    {
        _auth = auth;
    }

    /// <summary>供前端构造 Casdoor 授权地址与跳转回调所需的元数据。</summary>
    [HttpGet("config")]
    public IActionResult GetConfig() => MhopOk(_auth.CasdoorConfig(Request));

    /// <summary>生成一次性的签名 state（防 CSRF 登录），10 分钟内有效。</summary>
    [HttpGet("state")]
    public IActionResult GetState() => MhopOk(_auth.CasdoorState());

    /// <summary>授权码回调：换取 Casdoor 用户信息 → 绑定/创建本地账号 → 签发 MHOP JWT。</summary>
    [HttpPost("callback")]
    public async Task<IActionResult> Callback([FromBody] CasdoorCallbackIn body)
        => MhopOk(await _auth.CasdoorCallbackAsync(body, Request));
}
