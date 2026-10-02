using ClouderyApi.Modules.Cloudery.Api.Contracts;
using ClouderyApi.Modules.Cloudery.Application;

namespace ClouderyApi.Modules.Cloudery.Application;

/// <summary>
/// 登录用户测评结果的云端存档用例（exam/results）。归属过滤、幂等合并与配额仍由
/// ExamResultService 兜底，这里只做用例编排：空批只取回、单条上传包装成一批。
/// 被拒绝的内容仍抛 ExamResultRejectedException（模块内异常，控制器翻译成 400 裸对象）。
/// </summary>
public sealed class ExamResultAppService(ExamResultService service)
{
    /// <summary>该用户的云端记录（新的在前）。</summary>
    public Task<List<ExamResultOut>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
        => service.ListAsync(userId, cancellationToken);

    /// <summary>
    /// 批量同步；records 为空时只取回云端全量（换设备后第一次打开页面即此情形）。
    /// </summary>
    public async Task<ExamResultSyncOut> SyncAsync(
        Guid userId,
        IReadOnlyList<ExamResultIn>? records,
        CancellationToken cancellationToken = default)
    {
        if (records is null || records.Count == 0)
        {
            var only = await service.ListAsync(userId, cancellationToken);
            return new ExamResultSyncOut { Success = true, Uploaded = 0, Total = only.Count, Results = only };
        }

        return await service.SyncAsync(userId, records, cancellationToken);
    }

    /// <summary>按云端 Id 或站点本机记录键删除一条；false 表示记录不存在。</summary>
    public Task<bool> DeleteAsync(Guid userId, string idOrClientKey, CancellationToken cancellationToken = default)
        => service.DeleteAsync(userId, idOrClientKey, cancellationToken);

    /// <summary>清空该用户的云端记录，返回删除条数。</summary>
    public Task<int> ClearAsync(Guid userId, CancellationToken cancellationToken = default)
        => service.ClearAsync(userId, cancellationToken);
}
