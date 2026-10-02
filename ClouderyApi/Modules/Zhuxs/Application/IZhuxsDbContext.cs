using ClouderyApi.Modules.Zhuxs.Domain;
// 命名空间段 Application 与实体名 Application 同名，必须用别名（CS0118）
using DomainApplication = ClouderyApi.Modules.Zhuxs.Domain.Application;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Zhuxs.Application;

/// <summary>
/// Zhuxs 域的持久化边界（Stage 5 §5.1 第一步：代码级隔离，零迁移）。
/// 应用服务只依赖本接口，ClouderyApiContext 现同时实现本接口与 IClouderyDbContext；
/// 后续物理拆分时只需把实现换成独立的 ZhuxsContext，应用层无需再改。
/// </summary>
public interface IZhuxsDbContext
{
    DbSet<Whitelist> ZhuxsWhitelists { get; }
    DbSet<Term> ZhuxsTerms { get; }
    DbSet<DomainApplication> ZhuxsApplications { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
