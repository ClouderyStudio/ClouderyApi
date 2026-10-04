using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application.Mapping;
using ClouderyApi.Modules.Scforge.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>
/// 受支持的游戏版本：读取（所有人）+ 维护（仅超级管理员）。
///
/// 生存战争的版本号每两三周就往前推一格，写死在代码里意味着每次都要发版；
/// 因此改成一张表，超管在后台自行添加，插件 / 版本的兼容声明都以它为准。
/// </summary>
public sealed class ScforgeGameVersionAppService(
    IScforgeDbContext db,
    ILogger<ScforgeGameVersionAppService> logger)
{
    /// <summary>全部受支持的版本键，新的在前（发布校验与排序的唯一来源）。</summary>
    public async Task<List<string>> KeysAsync(CancellationToken cancellationToken = default) =>
        await db.ScforgeGameVersions
            .AsNoTracking()
            .OrderByDescending(v => v.SortOrder)
            .ThenByDescending(v => v.Version)
            .Select(v => v.Version)
            .ToListAsync(cancellationToken);

    /// <summary>公开列表（发布页与筛选面板的取值来源）。</summary>
    public async Task<List<ScforgeGameVersionDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.ScforgeGameVersions
            .AsNoTracking()
            .OrderByDescending(v => v.SortOrder)
            .ThenByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

        return rows.Select(v => ToDto(v, 0)).ToList();
    }

    /// <summary>后台列表：附带被多少资源引用，便于判断能不能删。</summary>
    public async Task<List<ScforgeGameVersionDto>> ListForAdminAsync(
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireAdmin();

        var rows = await db.ScforgeGameVersions
            .AsNoTracking()
            .OrderByDescending(v => v.SortOrder)
            .ThenByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

        var result = new List<ScforgeGameVersionDto>(rows.Count);
        foreach (var row in rows)
        {
            result.Add(ToDto(row, await CountUsagesAsync(row.Version, cancellationToken)));
        }

        return result;
    }

    /// <summary>添加版本（仅超级管理员）。</summary>
    public async Task<ScforgeGameVersionDto> CreateAsync(
        ScforgeGameVersionIn form,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireSuper();

        var version = (form.Version ?? string.Empty).Trim();
        if (version.Length == 0) throw new ScforgeRuleException("请填写游戏版本号");
        if (version.Length > 32) throw new ScforgeRuleException("游戏版本号不能超过 32 个字符");
        if (!ScforgeCatalog.IsGameVersionFormat(version)) throw new ScforgeRuleException("版本号格式形如 x26.07.01");

        if (await db.ScforgeGameVersions.AnyAsync(v => v.Version == version, cancellationToken))
        {
            throw new ScforgeApiException(409, $"游戏版本「{version}」已经存在");
        }

        var maxOrder = await db.ScforgeGameVersions.MaxAsync(v => (int?)v.SortOrder, cancellationToken) ?? 0;
        var entity = new ScforgeGameVersion
        {
            Id = Guid.NewGuid(),
            Version = version,
            SortOrder = maxOrder + 1,
            Beta = form.Beta ?? false,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = actor.DisplayName,
        };

        db.ScforgeGameVersions.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("SCForge：{Actor} 添加了游戏版本 {Version}（内测={Beta}）", actor.DisplayName, version, entity.Beta);

        return ToDto(entity, 0);
    }

    /// <summary>删除版本（仅超级管理员）；仍被资源引用时拒绝，避免留下「声明了不存在的版本」。</summary>
    public async Task DeleteAsync(Guid id, ScforgeAdminContext admin, CancellationToken cancellationToken = default)
    {
        admin.RequireSuper();

        var entity = await db.ScforgeGameVersions.FirstOrDefaultAsync(v => v.Id == id, cancellationToken)
                     ?? throw new ScforgeApiException(404, "游戏版本不存在");

        var used = await CountUsagesAsync(entity.Version, cancellationToken);
        if (used > 0)
        {
            throw new ScforgeRuleException($"还有 {used} 个资源声明兼容 {entity.Version}，不能删除");
        }

        db.ScforgeGameVersions.Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("SCForge：{Actor} 删除了游戏版本 {Version}", admin.DisplayName, entity.Version);
    }

    /// <summary>有多少资源声明兼容该版本（主版本等值 + 兼容列表 LIKE）。</summary>
    private async Task<int> CountUsagesAsync(string version, CancellationToken cancellationToken)
    {
        var pattern = $"%|{version}|%";
        return await db.ScforgePlugins.CountAsync(
            p => p.GameVersion == version || EF.Functions.Like(p.GameVersionsText, pattern),
            cancellationToken);
    }

    private static ScforgeGameVersionDto ToDto(ScforgeGameVersion entity, int usageCount) => new()
    {
        Id = entity.Id.ToString(),
        Version = entity.Version,
        SortOrder = entity.SortOrder,
        Beta = entity.Beta,
        CreatedAt = ScforgeMapper.ToBeijing(entity.CreatedAt),
        CreatedBy = entity.CreatedBy,
        UsageCount = usageCount,
    };
}
