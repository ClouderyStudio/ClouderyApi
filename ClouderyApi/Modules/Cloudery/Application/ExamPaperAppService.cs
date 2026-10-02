using ClouderyApi.Data;
using ClouderyApi.Modules.Cloudery.Domain;
using ClouderyApi.Modules.Cloudery.Api.Contracts;
using ClouderyApi.Modules.Cloudery.Application.Mapping;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Cloudery.Application;

/// <summary>试卷写操作结果：控制器据此映射 201 / 204 / 404 / 409，应用层不感知 HTTP 与中文文案。</summary>
public enum ExamPaperWriteOutcome
{
    /// <summary>写入成功。</summary>
    Ok,
    /// <summary>目标试卷不存在。</summary>
    NotFound,
    /// <summary>试卷 ID 已存在。</summary>
    DuplicateId,
    /// <summary>保存失败（唯一键 / 数据库冲突）。</summary>
    SaveFailed,
    /// <summary>并发冲突。</summary>
    ConcurrencyConflict,
}

/// <summary>写操作返回值：<see cref="Paper"/> 仅在 Ok 时有值。</summary>
public sealed record ExamPaperWriteResult(ExamPaperWriteOutcome Outcome, ExamPaperFullView? Paper);

/// <summary>
/// 内部测试试卷用例（exam/ExamPapers）：公开读（不含答案）、管理端读写与判分编排。
/// 入参仍为 ExamPaper 实体（请求体绑定与校验语义不得改变），出参一律为视图 DTO。
/// </summary>
public sealed class ExamPaperAppService(ClouderyApiContext db)
{
    /// <summary>公开列表（不含 Answer / Note）。</summary>
    public async Task<List<ExamPaperView>> ListAsync(CancellationToken cancellationToken = default)
    {
        var papers = await db.ExamPapers
            .AsNoTracking()
            .OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);
        return papers.Select(ExamPaperMapper.ToView).ToList();
    }

    /// <summary>公开单份（不含 Answer / Note）；null 表示不存在（控制器映射为 404）。</summary>
    public async Task<ExamPaperView?> FindViewAsync(string id, CancellationToken cancellationToken = default)
    {
        var paper = await db.ExamPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return paper is null ? null : ExamPaperMapper.ToView(paper);
    }

    /// <summary>管理端单份（含 Answer / Note）；null 表示不存在（控制器映射为 404）。</summary>
    public async Task<ExamPaperFullView?> FindFullAsync(string id, CancellationToken cancellationToken = default)
    {
        var paper = await db.ExamPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return paper is null ? null : ExamPaperMapper.ToFullView(paper);
    }

    /// <summary>服务端判分；null 表示试卷不存在（控制器映射为 404）。</summary>
    public async Task<ExamGradeResult?> GradeAsync(string id, GradeRequest request, CancellationToken cancellationToken = default)
    {
        var paper = await db.ExamPapers.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        return paper is null ? null : ExamPaperGrader.Grade(paper, request);
    }

    /// <summary>新增试卷：Id 留空时由服务端生成；UpdatedAt 由服务端盖章。</summary>
    public async Task<ExamPaperWriteResult> CreateAsync(ExamPaper paper, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(paper.Id))
            paper.Id = Guid.NewGuid().ToString("N");

        if (await db.ExamPapers.AnyAsync(p => p.Id == paper.Id, cancellationToken))
            return new(ExamPaperWriteOutcome.DuplicateId, null);

        paper.UpdatedAt = DateTime.UtcNow;
        db.ExamPapers.Add(paper);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return new(ExamPaperWriteOutcome.SaveFailed, null);
        }

        return new(ExamPaperWriteOutcome.Ok, ExamPaperMapper.ToFullView(paper));
    }

    /// <summary>整卷覆盖：只改 Name / Sections / UpdatedAt。</summary>
    public async Task<ExamPaperWriteOutcome> UpdateAsync(string id, ExamPaper paper, CancellationToken cancellationToken = default)
    {
        var existing = await db.ExamPapers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (existing == null) return ExamPaperWriteOutcome.NotFound;

        existing.Name = paper.Name;
        existing.Sections = paper.Sections;
        existing.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ExamPaperWriteOutcome.ConcurrencyConflict;
        }

        return ExamPaperWriteOutcome.Ok;
    }

    /// <summary>返回 false 表示试卷不存在（控制器映射为 404）。</summary>
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var paper = await db.ExamPapers.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (paper == null) return false;

        db.ExamPapers.Remove(paper);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
