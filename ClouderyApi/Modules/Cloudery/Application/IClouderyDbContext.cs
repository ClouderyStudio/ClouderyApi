using ClouderyApi.Modules.Cloudery.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Cloudery.Application;

/// <summary>
/// Cloudery 域的持久化边界（Stage 5 §5.1 第一步：代码级隔离，零迁移）。
/// 应用服务只依赖本接口，ClouderyApiContext 现同时实现本接口与 IZhuxsDbContext；
/// 后续物理拆分时只需把实现换成独立的 ClouderyContext，应用层无需再改。
/// </summary>
public interface IClouderyDbContext
{
    DbSet<Member> ClouderyMembers { get; }
    DbSet<ExamPaper> ExamPapers { get; }
    DbSet<ExamResult> ExamResults { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
