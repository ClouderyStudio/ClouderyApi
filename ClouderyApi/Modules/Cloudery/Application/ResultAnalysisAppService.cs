using ClouderyApi.Modules.Cloudery.Api.Contracts;
using ClouderyApi.Modules.Cloudery.Application;

namespace ClouderyApi.Modules.Cloudery.Application;

/// <summary>
/// 测评结果的 AI 解读用例（exam/result-analysis）。提示词与本地兜底仍由
/// ResultAnalysisService 负责，这里只做用例入口校验与编排。
/// </summary>
public sealed class ResultAnalysisAppService(ResultAnalysisService service)
{
    /// <summary>
    /// 解读一份量表结果；请求体缺失或缺少 testId 时返回 null（控制器映射为 400 裸对象）。
    /// </summary>
    public async Task<ResultAnalysisOut?> AnalyzeAsync(
        ResultAnalysisIn? body,
        CancellationToken cancellationToken = default)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.TestId)) return null;

        return await service.AnalyzeAsync(body, cancellationToken);
    }
}
