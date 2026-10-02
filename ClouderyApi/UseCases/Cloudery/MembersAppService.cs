using ClouderyApi.Data;
using ClouderyApi.Models.Cloudery;
using ClouderyApi.Models.Cloudery.DTOs;
using ClouderyApi.UseCases.Cloudery.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.UseCases.Cloudery;

/// <summary>
/// 成员管理用例（cloudery/members）：列表/详情公开可读，写操作由控制器上的 [AdminOnly] 把关。
/// 只回传输出 DTO，不再把 EF 实体交给控制器。
/// </summary>
public sealed class MembersAppService(ClouderyApiContext db)
{
    public async Task<List<MemberOut>> ListAsync(CancellationToken cancellationToken = default)
    {
        var members = await db.ClouderyMembers
            .AsNoTracking()
            .OrderBy(x => x.Name)
            .Take(1000)
            .ToListAsync(cancellationToken);
        return members.Select(MemberMapper.ToOut).ToList();
    }

    /// <summary>返回 null 表示成员不存在（控制器映射为 404）。</summary>
    public async Task<MemberOut?> FindAsync(string id, CancellationToken cancellationToken = default)
    {
        var member = await db.ClouderyMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return member is null ? null : MemberMapper.ToOut(member);
    }

    /// <summary>返回 false 表示成员不存在（控制器映射为 404）。</summary>
    public async Task<bool> UpdateAsync(string id, MemberDto dto, CancellationToken cancellationToken = default)
    {
        var member = await db.ClouderyMembers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (member == null) return false;

        member.Name = dto.Name;
        member.Position = dto.Position;
        member.Description = dto.Description;
        member.Socials = dto.Socials;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // 记录已被删除 → 404；否则维持原有的抛错语义（交给全局异常处理）。
            if (await db.ClouderyMembers.AnyAsync(x => x.Id == id, cancellationToken)) throw;
            return false;
        }

        return true;
    }

    /// <summary>保存冲突时抛 <see cref="ClouderyWriteConflictException"/>（控制器映射为 409）。</summary>
    public async Task<MemberOut> CreateAsync(MemberDto dto, CancellationToken cancellationToken = default)
    {
        var member = new Member
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = dto.Name,
            Position = dto.Position,
            Description = dto.Description,
            Socials = dto.Socials
        };

        db.ClouderyMembers.Add(member);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            throw new ClouderyWriteConflictException("记录冲突", ex);
        }

        return MemberMapper.ToOut(member);
    }

    /// <summary>返回 false 表示成员不存在（控制器映射为 404）。</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var member = await db.ClouderyMembers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (member == null) return false;

        db.ClouderyMembers.Remove(member);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
