using ClouderyApi.Shared.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ClouderyApi.Modules.SurvivalCraft.Api;

[Route("sc/[controller]")]
[ApiController]
[Authorize]
public class ServerController : ControllerBase
{
    /// <summary>转发 SCKEY 请求所用的命名 HttpClient（在 Program.cs 注册）。</summary>
    public const string HttpClientName = "SckeyServer";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SckeyOptions _sckey;

    public ServerController(IHttpClientFactory httpClientFactory, IOptions<SckeyOptions> sckeyOptions)
    {
        _httpClientFactory = httpClientFactory;
        _sckey = sckeyOptions.Value;
    }

    // 配置键以 appsettings.json 实际定义的 Env:* 为准；旧代码读 SurvivalCraft:*，导致令牌恒为空、
    // Authorization 头从未发出。旧键名回退已移至 SckeyOptions 的 PostConfigure（见 Program.cs）。
    private string ApiBase => _sckey.ApiBase ?? "https://api.sckey.net";

    private string ApiToken => _sckey.BearerToken ?? "";

    /// <summary>
    /// 校验转发路径，防止路径穿越（SSRF 保护）
    /// </summary>
    private static bool IsValidServerPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.Contains("..", StringComparison.Ordinal)) return false;
        if (path.Contains('\\') || path.Contains("//")) return false;
        // 只允许字母数字、下划线、连字符、点（单段路径）
        return path.All(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.');
    }

    /// <summary>向 SCKEY 后端发起请求；令牌非空时按 Bearer 方案附加 Authorization 头。</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string requestLink, HttpContent? content)
    {
        using var request = new HttpRequestMessage(method, requestLink);
        if (content is not null) request.Content = content;

        var token = ApiToken;
        if (!string.IsNullOrEmpty(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await _httpClientFactory.CreateClient(HttpClientName).SendAsync(request);
    }

    /// <summary>
    /// 转发 POST 请求到 SCKEY 后端
    /// </summary>
    [HttpPost]
    [Route("{path}")]
    public async Task<IActionResult> PostFromServerPath(string path, [FromBody] JsonElement? body)
    {
        if (!IsValidServerPath(path))
            return BadRequest(new { success = false, message = "非法的服务器路径" });

        var requestLink = ApiBase + $"/server/{path}";
        var content = new StringContent(body?.ToString() ?? "", Encoding.UTF8, "application/json");

        try
        {
            var response = await SendAsync(HttpMethod.Post, requestLink, content);
            var responseBody = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, responseBody);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { success = false, message = $"后端请求失败: {ex.Message}" });
        }
    }

    [HttpGet]
    [Route("get/{path}")]
    public async Task<IActionResult> GetFromLocalServer(string path)
        => await GetFromServerPath(path);

    /// <summary>
    /// 转发 GET 请求到 SCKEY 后端
    /// </summary>
    [HttpGet]
    [Route("{path}")]
    public async Task<IActionResult> GetFromServerPath(string path)
    {
        if (!IsValidServerPath(path))
            return BadRequest(new { success = false, message = "非法的服务器路径" });

        var requestLink = ApiBase + $"/server/{path}";

        try
        {
            var response = await SendAsync(HttpMethod.Get, requestLink, null);
            var responseBody = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, responseBody);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { success = false, message = $"后端请求失败: {ex.Message}" });
        }
    }
}
