using Casdoor.Client;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Controllers.Auth;

[Route("identity/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    static IConfigurationRoot config = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory()).AddJsonFile("appsettings.json").Build();

    static CasdoorOptions options = new CasdoorOptions
    {
#pragma warning disable CS8601 // 引用类型赋值可能为 null
        Endpoint = config["Casdoor:Endpoint"],
        OrganizationName = config["Casdoor:OrganizationName"],
        ApplicationName = config["Casdoor:ApplicationName"],
        ApplicationType = config["Casdoor:ApplicationType"],
        ClientId = config["Casdoor:ClientId"],
        ClientSecret = config["Casdoor:ClientSecret"],
        CallbackPath = config["Casdoor:CallbackPath"],
#pragma warning restore CS8601
    };

    static CasdoorClient client = new(new HttpClient(), options);

    /// <summary>
    /// OAuth2 回调接口 - 用 code 换取用户信息并建立会话
    /// </summary>
    [HttpPost("callback")]
    public async Task<IActionResult> Callback([FromBody] OAuthCallbackRequest request)
    {
        try
        {
            if (string.IsNullOrEmpty(request.Code))
            {
                return BadRequest(new { message = "授权码不能为空" });
            }

            var tokenResponse = await client.RequestAuthorizationCodeTokenAsync(
                code: request.Code,
                redirectUri: request.RedirectUri
            );

            var token = tokenResponse.AccessToken;

            if (tokenResponse == null || string.IsNullOrEmpty(token))
            {
                return BadRequest(new { message = "换取访问令牌失败" });
            }

            client.SetBearerToken(token);

            var user = client.ParseJwtToken(token, false);

            if (user == null || string.IsNullOrEmpty(user.Id))
            {
                return BadRequest(new { message = "获取用户信息失败" });
            }

            return Ok(new
            {
                success = true,
                message = "成功",
                user = new
                {
                    id = user.Id,
                    username = user.Name,
                    email = user.Email,
                    avatar = user.Avatar,
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "服务器内部错误" });
        }
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
}