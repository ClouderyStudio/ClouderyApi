using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;

namespace ClouderyApi.Controllers.SurvivalCraft;

[Route("sc/[controller]")]
[ApiController]
[Authorize]
public class ServerController : ControllerBase
{
    private static readonly IConfiguration _config = new ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json")
        .Build();

    private static readonly string API_BASE = _config["SurvivalCraft:SCKEY_API_BASE"] ?? "https://api.sckey.net";
    private static readonly string API_TOKEN = _config["SurvivalCraft:SCKEY_BEARER_TOKEN"] ?? "";

    // 静态共享 HttpClient，避免每个请求新建导致套接字耗尽
    private static readonly HttpClient _client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient();
        if (!string.IsNullOrEmpty(API_TOKEN))
        {
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", API_TOKEN);
        }
        return client;
    }

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

    /// <summary>
    /// 转发 POST 请求到 SCKEY 后端
    /// </summary>
    [HttpPost]
    [Route("{path}")]
    public async Task<IActionResult> PostFromServerPath(string path, [FromBody] JsonElement? body)
    {
        if (!IsValidServerPath(path))
            return BadRequest(new { success = false, message = "非法的服务器路径" });

        var requestLink = API_BASE + $"/server/{path}";
        var content = new StringContent(body?.ToString() ?? "", Encoding.UTF8, "application/json");

        try
        {
            var response = await _client.PostAsync(requestLink, content);
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

        var requestLink = API_BASE + $"/server/{path}";

        try
        {
            var response = await _client.GetAsync(requestLink);
            var responseBody = await response.Content.ReadAsStringAsync();
            return StatusCode((int)response.StatusCode, responseBody);
        }
        catch (HttpRequestException ex)
        {
            return StatusCode(502, new { success = false, message = $"后端请求失败: {ex.Message}" });
        }
    }
}