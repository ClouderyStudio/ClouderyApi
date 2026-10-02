using ClouderyApi.Data;
using ClouderyApi.Modules.Zhuxs.Domain;
using ClouderyApi.Modules.Zhuxs.Api.Contracts;
using ClouderyApi.Modules.Zhuxs.Application.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Zhuxs.Application;

/// <summary>
/// 开发进度（zhuxs/terms）用例：读接口匿名可访问，写操作仅管理员。
/// 列表按 RecordDate 倒序并截断 1000 条；主键由服务端生成。
/// </summary>
public class TermsAppService(ClouderyApiContext db)
{
    /// <summary>按记录日期倒序的列表（最多 1000 条）。</summary>
    public async Task<List<TermOut>> ListAsync(CancellationToken cancellationToken = default)
    {
        var terms = await db.ZhuxsTerms
            .OrderByDescending(x => x.RecordDate)
            .Take(1000)
            .ToListAsync(cancellationToken);
        return terms.Select(TermMapper.ToOut).ToList();
    }

    /// <summary>按 Id 取单条；不存在返回 null（控制器映射为 404 空体）。</summary>
    public async Task<TermOut?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var term = await db.ZhuxsTerms.FindAsync([id], cancellationToken);
        return term is null ? null : TermMapper.ToOut(term);
    }

    /// <summary>
    /// 覆盖式更新：只改 RecordDate / Description / Information / Files（主键与其它字段不可改）。
    /// false 表示不存在。
    /// </summary>
    public async Task<bool> UpdateAsync(string id, TermDto dto, CancellationToken cancellationToken = default)
    {
        var term = await db.ZhuxsTerms.FindAsync([id], cancellationToken);
        if (term is null) return false;

        term.RecordDate = dto.RecordDate;
        term.Description = dto.Description;
        term.Information = dto.Information;
        term.Files = dto.Files;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (await db.ZhuxsTerms.AnyAsync(e => e.Id == id, cancellationToken)) throw;
            return false;
        }

        return true;
    }

    /// <summary>新增一条进度；唯一键冲突抛 <see cref="ZhuxsWriteConflictException"/>。</summary>
    public async Task<TermOut> CreateAsync(TermDto dto, CancellationToken cancellationToken = default)
    {
        var term = new Term
        {
            Id = Guid.NewGuid().ToString("N"),
            RecordDate = dto.RecordDate,
            Description = dto.Description,
            Information = dto.Information,
            Files = dto.Files
        };

        db.ZhuxsTerms.Add(term);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new ZhuxsWriteConflictException("记录冲突", ex);
        }

        return TermMapper.ToOut(term);
    }

    /// <summary>删除一条；false 表示不存在（控制器映射为 404 空体）。</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var term = await db.ZhuxsTerms.FindAsync([id], cancellationToken);
        if (term is null) return false;

        db.ZhuxsTerms.Remove(term);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
