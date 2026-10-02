using ClouderyApi.Modules.Cloudery.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Cloudery.Application;

/// <summary>
/// Cloudery 域的持久化边界（Stage 5 §5.1 第一步：代码级隔离，零迁移）。
/// 应用服务只依赖本接口，实现为独立的 ClouderyContext（Stage 5 §5.1 第二步已完成拆分）；
/// 
/// </summary>
public interface IClouderyDbContext
{
    DbSet<Member> ClouderyMembers { get; }
    DbSet<ExamPaper> ExamPapers { get; }
    DbSet<ExamResult> ExamResults { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
