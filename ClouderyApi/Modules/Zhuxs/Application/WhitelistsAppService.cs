using ClouderyApi.Data;
using ClouderyApi.Modules.Zhuxs.Domain;
using ClouderyApi.Modules.Zhuxs.Api.Contracts;
using ClouderyApi.Modules.Zhuxs.Application.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Zhuxs.Application;

/// <summary>
/// 白名单（邀请码）用例（zhuxs/whitelists），仅管理员可访问。
/// 读写都用仓储式筛选：列表按 Code 排序并截断 1000 条，主键由服务端生成（客户端不可指定）。
/// </summary>
public class WhitelistsAppService(ClouderyApiContext db)
{
    /// <summary>按邀请码排序的列表（最多 1000 条，避免无界返回全表）。</summary>
    public async Task<List<WhitelistOut>> ListAsync(CancellationToken cancellationToken = default)
    {
        var whitelists = await db.ZhuxsWhitelists
            .OrderBy(w => w.Code)
            .Take(1000)
            .ToListAsync(cancellationToken);
        return whitelists.Select(WhitelistMapper.ToOut).ToList();
    }

    /// <summary>按 Id 取单条；不存在返回 null（控制器映射为 404 空体）。</summary>
    public async Task<WhitelistOut?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var whitelist = await db.ZhuxsWhitelists.FindAsync([id], cancellationToken);
        return whitelist is null ? null : WhitelistMapper.ToOut(whitelist);
    }

    /// <summary>新增一条邀请码；唯一键冲突抛 <see cref="ZhuxsWriteConflictException"/>。</summary>
    public async Task<WhitelistOut> CreateAsync(WhitelistDto dto, CancellationToken cancellationToken = default)
    {
        // 主键由服务端生成，客户端不可指定（防 over-posting）
        var whitelist = new Whitelist { Id = Guid.NewGuid().ToString("N"), Code = dto.Code };
        db.ZhuxsWhitelists.Add(whitelist);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new ZhuxsWriteConflictException("记录冲突", ex);
        }

        return WhitelistMapper.ToOut(whitelist);
    }

    /// <summary>删除一条；false 表示不存在（控制器映射为 404 空体）。</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var whitelist = await db.ZhuxsWhitelists.FindAsync([id], cancellationToken);
        if (whitelist is null) return false;

        db.ZhuxsWhitelists.Remove(whitelist);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
