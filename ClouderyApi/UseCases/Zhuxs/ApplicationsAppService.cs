using ClouderyApi.Data;
using ClouderyApi.Models.Zhuxs;
using ClouderyApi.Models.Zhuxs.DTOs;
using ClouderyApi.UseCases.Zhuxs.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.UseCases.Zhuxs;

/// <summary>
/// 报名申请（zhuxs/applications）用例：读接口匿名可访问，写操作仅管理员。
/// 列表按提交时间倒序并截断 1000 条；主键与审核状态由服务端掌管。
/// </summary>
public class ApplicationsAppService(ClouderyApiContext db)
{
    /// <summary>按提交时间倒序的列表（最多 1000 条）。</summary>
    public async Task<List<ApplicationOut>> ListAsync(CancellationToken cancellationToken = default)
    {
        var applications = await db.ZhuxsApplications
            .OrderByDescending(a => a.SubmissionDate)
            .Take(1000)
            .ToListAsync(cancellationToken);
        return applications.Select(ApplicationMapper.ToOut).ToList();
    }

    /// <summary>按 Id 取单条；不存在返回 null（控制器映射为 404 空体）。</summary>
    public async Task<ApplicationOut?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var application = await db.ZhuxsApplications.FindAsync([id], cancellationToken);
        return application is null ? null : ApplicationMapper.ToOut(application);
    }

    /// <summary>
    /// 更新分享内容；Passed 仅在请求携带该字段时改写（审核通过/驳回走同一个接口）。
    /// false 表示不存在。
    /// </summary>
    public async Task<bool> UpdateAsync(string id, ApplicationDto dto, CancellationToken cancellationToken = default)
    {
        var application = await db.ZhuxsApplications.FindAsync([id], cancellationToken);
        if (application is null) return false;

        application.Sharables = dto.Sharables;
        if (dto.Passed.HasValue)
            application.Passed = dto.Passed.Value;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (await db.ZhuxsApplications.AnyAsync(e => e.Id == id, cancellationToken)) throw;
            return false;
        }

        return true;
    }

    /// <summary>新增一条报名；唯一键冲突抛 <see cref="ZhuxsWriteConflictException"/>。</summary>
    public async Task<ApplicationOut> CreateAsync(ApplicationDto dto, CancellationToken cancellationToken = default)
    {
        // 主键服务端生成；Passed 恒为 false，需管理员在 PUT 阶段审核通过（防 over-posting 绕过审核）
        var application = new Application
        {
            Id = Guid.NewGuid().ToString("N"),
            Passed = false,
            SubmissionDate = DateTime.UtcNow,
            Sharables = dto.Sharables
        };

        db.ZhuxsApplications.Add(application);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new ZhuxsWriteConflictException("记录冲突", ex);
        }

        return ApplicationMapper.ToOut(application);
    }

    /// <summary>删除一条；false 表示不存在（控制器映射为 404 空体）。</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var application = await db.ZhuxsApplications.FindAsync([id], cancellationToken);
        if (application is null) return false;

        db.ZhuxsApplications.Remove(application);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
