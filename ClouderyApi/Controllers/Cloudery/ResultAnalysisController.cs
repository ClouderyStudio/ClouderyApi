using ClouderyApi.Controllers.Filters;
using ClouderyApi.Models.Cloudery.DTOs;
using ClouderyApi.UseCases.Cloudery;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Controllers.Cloudery;

/// <summary>
/// 测评结果的 AI 解读。结果页无需登录即可使用，因此按 IP 单独限流（比全局 300 次/分钟更严），
/// 返回裸对象，与 Cloudery 模块其它接口保持一致（不用 MhopOk 的蛇形包装）。
/// </summary>
[ApiController]
[Route("exam/result-analysis")]
public sealed class ResultAnalysisController(ResultAnalysisAppService app) : ControllerBase
{
    /// <summary>解读一份测评结果；模型不可用时返回本地兜底文本（engine=local），不会以 5xx 结束。</summary>
    [HttpPost]
    [IpRateLimit(MaxRequests = 8, WindowSeconds = 300)]
    public async Task<ActionResult<ResultAnalysisOut>> Analyze(
        [FromBody] ResultAnalysisIn body, CancellationToken cancellationToken)
    {
        var result = await app.AnalyzeAsync(body, cancellationToken);
        if (result is null)
        {
            return BadRequest(new { success = false, message = "缺少量表标识（testId）" });
        }

        return Ok(result);
    }
}
