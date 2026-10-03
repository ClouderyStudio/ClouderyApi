using ClouderyApi.Modules.Mhop.Application;
using ClouderyApi.Modules.Mhop.Api.Contracts;
using ClouderyApi.Shared.Filters;
using Microsoft.AspNetCore.Mvc;

namespace ClouderyApi.Modules.Mhop.Api;

/// <summary>
/// AI 心理评估：标准量表计分 + AI 解读（对应 Python 后端 routers/assessment.py）。
/// 匿名用户服务端不做任何持久化（隐私优先）；登录用户仅在显式勾选 save_to_cloud 时写入云端。
/// </summary>
[ApiController]
[Route("mhop/assessments")]
public class MhopAssessmentController : MhopControllerBase
{
    private readonly AssessmentAppService _assessments;

    public MhopAssessmentController(AssessmentAppService assessments)
    {
        _assessments = assessments;
    }

    [HttpGet("scales")]
    public IActionResult GetScales() => MhopOk(_assessments.Scales());

    /// <summary>
    /// 提交一次评估。每次调用都会同步等待一次大模型返回，因此必须限流：
    /// 该端点匿名可用，不限流等于把模型账单对外开放。
    /// </summary>
    [HttpPost]
    [IpRateLimit(MaxRequests = 8, WindowSeconds = 300, UseMhopErrorShape = true)]
    public async Task<IActionResult> Submit([FromBody] AssessmentIn body)
        => MhopOk(await _assessments.SubmitAsync(body));

    [HttpGet("mine")]
    public async Task<IActionResult> MyAssessments()
        => MhopOk(await _assessments.MineAsync());
}
