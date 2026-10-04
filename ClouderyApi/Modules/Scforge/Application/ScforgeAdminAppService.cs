using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application.Mapping;
using ClouderyApi.Modules.Scforge.Domain;
using ClouderyApi.Modules.Scforge.Infrastructure;
using ClouderyApi.Shared.Directory;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>
/// SCForge 后台面板的用例编排：审核队列、审核动作、内容管理、管理员与权限管理。
///
/// 权限模型（见 <see cref="ScforgeAdminContext"/>）：
///   • 进入面板：IsAdmin；
///   • 审核通过 / 驳回：review 权限；
///   • 编辑 / 删除任意插件：content 权限；
///   • 授予、调整、撤销管理员：只有超级管理员。
/// </summary>
public sealed class ScforgeAdminAppService(
    IScforgeDbContext db,
    ScforgeAdminAccessor adminAccessor,
    IUserDirectory directory,
    ScforgePluginAppService plugins,
    ILogger<ScforgeAdminAppService> logger)
{
    /* ============================ 面板身份 ============================ */

    public async Task<ScforgeAdminMeDto> GetContextAsync(CancellationToken cancellationToken = default)
    {
        var admin = await adminAccessor.ResolveAsync(cancellationToken);

        return new ScforgeAdminMeDto
        {
            IsAdmin = admin.IsAdmin,
            IsSuperAdmin = admin.IsSuperAdmin,
            Username = admin.IsAdmin ? admin.DisplayName : string.Empty,
            Permissions = admin.IsAdmin ? [.. admin.Permissions] : [],
            Catalog = ScforgePermissionSet.All
                .Select(code => new ScforgePermissionDto
                {
                    Key = code,
                    Label = ScforgePermissionSet.Labels.GetValueOrDefault(code, code),
                    Description = ScforgePermissionSet.Descriptions.GetValueOrDefault(code, string.Empty),
                })
                .ToList(),
        };
    }

    /* ============================ 概览 ============================ */

    public async Task<ScforgeAdminSummaryDto> SummaryAsync(
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireAdmin();

        var pluginGroups = await db.ScforgePlugins
            .AsNoTracking()
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new ScforgeAdminSummaryDto
        {
            PendingPlugins = pluginGroups.FirstOrDefault(g => g.Status == ScforgeContentStatus.Pending)?.Count ?? 0,
            PublishedPlugins = pluginGroups.FirstOrDefault(g => g.Status == ScforgeContentStatus.Published)?.Count ?? 0,
            RejectedPlugins = pluginGroups.FirstOrDefault(g => g.Status == ScforgeContentStatus.Rejected)?.Count ?? 0,
            TotalPlugins = pluginGroups.Sum(g => g.Count),
            PendingVersions = await db.ScforgeVersions
                .AsNoTracking()
                .CountAsync(v => v.Status == ScforgeContentStatus.Pending, cancellationToken),
            TotalDownloads = await db.ScforgePlugins.AsNoTracking().SumAsync(p => p.Downloads, cancellationToken),
            Admins = await db.ScforgeAdmins.AsNoTracking().CountAsync(cancellationToken),
        };
    }

    /* ============================ 审核队列 ============================ */

    /// <summary>待审（或指定状态）的插件队列，最老的排前面。</summary>
    public async Task<ScforgeAdminPluginPageDto> ReviewQueuePluginsAsync(
        string? status,
        int page,
        int pageSize,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireReview();

        var resolved = ScforgeContentStatus.Parse(status) ?? ScforgeContentStatus.Pending;
        var size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 50);
        var current = Math.Max(page, 1);

        var query = db.ScforgePlugins.AsNoTracking().Where(p => p.Status == resolved);
        var total = await query.CountAsync(cancellationToken);
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)size);
        if (current > totalPages) current = totalPages;

        var items = await query
            .OrderBy(p => p.UpdatedAt)
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new ScforgeAdminPluginPageDto
        {
            Items = await plugins.ProjectSummariesAsync(items, actor, cancellationToken),
            Page = current,
            PageSize = size,
            Total = total,
            TotalPages = totalPages,
        };
    }

    /// <summary>待审（或指定状态）的版本队列，最老的排前面。</summary>
    public async Task<ScforgeAdminVersionPageDto> ReviewQueueVersionsAsync(
        string? status,
        int page,
        int pageSize,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireReview();

        var resolved = ScforgeContentStatus.Parse(status) ?? ScforgeContentStatus.Pending;
        var size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 50);
        var current = Math.Max(page, 1);

        var query =
            from v in db.ScforgeVersions.AsNoTracking()
            join p in db.ScforgePlugins.AsNoTracking() on v.PluginId equals p.Id
            where v.Status == resolved
            select new { Version = v, Plugin = p };

        var total = await query.CountAsync(cancellationToken);
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)size);
        if (current > totalPages) current = totalPages;

        var rows = await query
            .OrderBy(x => x.Version.PublishedAt)
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new ScforgeAdminVersionPageDto
        {
            Items = rows.Select(x => ScforgeMapper.ToVersion(x.Version, x.Plugin)).ToList(),
            Page = current,
            PageSize = size,
            Total = total,
            TotalPages = totalPages,
        };
    }

    /* ============================ 审核动作 ============================ */

    /// <summary>通过或驳回一个插件提交。</summary>
    public async Task<ScforgePluginSummaryDto> ReviewPluginAsync(
        Guid pluginId,
        ScforgeReviewIn body,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireReview();

        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");

        var note = (body.Note ?? string.Empty).Trim();
        if (!body.Approve && note.Length == 0) throw new ScforgeRuleException("驳回时必须填写理由");
        if (note.Length > 500) throw new ScforgeRuleException("审核意见不能超过 500 个字符");

        plugin.Status = body.Approve ? ScforgeContentStatus.Published : ScforgeContentStatus.Rejected;
        plugin.ReviewNote = note.Length == 0 ? null : note;
        plugin.ReviewedAt = DateTime.UtcNow;
        plugin.ReviewedBy = admin.DisplayName;
        await db.SaveChangesAsync(cancellationToken);

        await plugins.RefreshGameVersionsTextAsync(plugin, cancellationToken);

        logger.LogInformation(
            "SCForge：{Admin} {Action} 插件 {Slug}",
            admin.DisplayName,
            body.Approve ? "通过" : "驳回",
            plugin.Slug);

        var projected = await plugins.ProjectSummariesAsync([plugin], actor, cancellationToken);
        return projected[0];
    }

    /// <summary>通过或驳回一个版本提交。</summary>
    public async Task<ScforgeVersionDto> ReviewVersionAsync(
        Guid versionId,
        ScforgeReviewIn body,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireReview();

        var version = await db.ScforgeVersions.FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken)
                      ?? throw new ScforgeApiException(404, "版本不存在");
        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == version.PluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");

        var note = (body.Note ?? string.Empty).Trim();
        if (!body.Approve && note.Length == 0) throw new ScforgeRuleException("驳回时必须填写理由");
        if (note.Length > 500) throw new ScforgeRuleException("审核意见不能超过 500 个字符");

        version.Status = body.Approve ? ScforgeContentStatus.Published : ScforgeContentStatus.Rejected;
        version.ReviewNote = note.Length == 0 ? null : note;
        version.ReviewedAt = DateTime.UtcNow;
        version.ReviewedBy = admin.DisplayName;
        await db.SaveChangesAsync(cancellationToken);

        await plugins.RefreshGameVersionsTextAsync(plugin, cancellationToken);

        logger.LogInformation(
            "SCForge：{Admin} {Action} {Slug} 的版本 {Version}",
            admin.DisplayName,
            body.Approve ? "通过" : "驳回",
            plugin.Slug,
            version.Version);

        return ScforgeMapper.ToVersion(version, plugin);
    }

    /* ============================ 内容管理 ============================ */

    /// <summary>全量插件列表（含待审与已驳回），供内容管理员巡检。</summary>
    public async Task<ScforgeAdminPluginPageDto> AllPluginsAsync(
        string? term,
        string? status,
        int page,
        int pageSize,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireAdmin();

        var size = Math.Clamp(pageSize <= 0 ? 20 : pageSize, 1, 50);
        var current = Math.Max(page, 1);

        var query = db.ScforgePlugins.AsNoTracking().AsQueryable();

        if (ScforgeContentStatus.Parse(status) is { } resolved)
        {
            query = query.Where(p => p.Status == resolved);
        }

        var keyword = (term ?? string.Empty).Replace("%", string.Empty).Replace("_", string.Empty).Trim();
        if (keyword.Length > 0)
        {
            var pattern = $"%{keyword}%";
            query = query.Where(p =>
                EF.Functions.Like(p.Name, pattern) ||
                EF.Functions.Like(p.Slug, pattern) ||
                EF.Functions.Like(p.AuthorName, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)size);
        if (current > totalPages) current = totalPages;

        var items = await query
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((current - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        return new ScforgeAdminPluginPageDto
        {
            Items = await plugins.ProjectSummariesAsync(items, actor, cancellationToken),
            Page = current,
            PageSize = size,
            Total = total,
            TotalPages = totalPages,
        };
    }

    /* ============================ 管理员管理 ============================ */

    /// <summary>管理员列表：库里的记录 + 仅由配置白名单引导出来的超管。</summary>
    public async Task<List<ScforgeAdminDto>> ListAdminsAsync(
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireSuper();

        var records = await db.ScforgeAdmins
            .AsNoTracking()
            .OrderBy(a => a.UserName)
            .ToListAsync(cancellationToken);

        var result = records.Select(ToDto).ToList();

        // 配置白名单里的账号没有库记录，但确实拥有超管权限，必须在后台可见（否则无法解释它的权限来源）。
        var whitelist = adminAccessor.ConfigWhitelist;
        var knownUserIds = records.Select(r => r.UserId).ToHashSet();
        foreach (var casdoorId in whitelist)
        {
            var user = await directory.FindAsync(casdoorId, cancellationToken);
            if (user is null) continue;
            if (!knownUserIds.Add(user.Id)) continue;

            result.Add(new ScforgeAdminDto
            {
                Id = string.Empty,
                UserId = user.Id.ToString(),
                Username = user.Username,
                Avatar = user.Avatar,
                Role = ScforgeAdminRoles.Super,
                IsSuperAdmin = true,
                Permissions = [.. ScforgePermissionSet.All],
                GrantedBy = "配置白名单",
                FromConfig = true,
                CreatedAt = null,
            });
        }

        return result.OrderByDescending(a => a.IsSuperAdmin).ThenBy(a => a.Username, StringComparer.Ordinal).ToList();
    }

    /// <summary>按关键词搜索可指定的用户，并标出其中已经是管理员的人。</summary>
    public async Task<List<ScforgeUserCandidateDto>> SearchUsersAsync(
        string? keyword,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireSuper();

        var users = await directory.SearchAsync(keyword, 20, cancellationToken);
        if (users.Count == 0) return [];

        var ids = users.Select(u => u.Id).ToList();
        var existing = await db.ScforgeAdmins
            .AsNoTracking()
            .Where(a => ids.Contains(a.UserId))
            .ToDictionaryAsync(a => a.UserId, a => a.Role, cancellationToken);

        return users
            .Select(u => new ScforgeUserCandidateDto
            {
                UserId = u.Id.ToString(),
                Username = u.Username,
                Email = u.Email,
                Avatar = u.Avatar,
                CurrentRole = existing.TryGetValue(u.Id, out var role) ? role : null,
            })
            .ToList();
    }

    /// <summary>指定一名管理员，或调整其角色与权限（仅超管）。</summary>
    public async Task<ScforgeAdminDto> UpsertAdminAsync(
        ScforgeAdminIn body,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireSuper();

        var key = (body.Username ?? string.Empty).Trim();
        if (key.Length == 0) throw new ScforgeRuleException("请填写要指定的用户名或邮箱");

        var role = (body.Role ?? ScforgeAdminRoles.Admin).Trim().ToLowerInvariant();
        if (!ScforgeAdminRoles.IsValid(role)) throw new ScforgeRuleException("角色只能是 super 或 admin");

        var user = await directory.FindAsync(key, cancellationToken)
                   ?? throw new ScforgeRuleException($"在用户目录里找不到「{key}」，请确认用户名或邮箱");

        var permissions = role == ScforgeAdminRoles.Super
            ? ScforgePermissionSet.All.ToList()
            : ScforgePermissionSet.From(body.Permissions).Codes.ToList();

        // 不允许把自己从超管降级：白名单引导的超管可能还没有库记录，所以按「当前是否超管」判断，
        // 而不是「这条记录是否已是超管」。
        if (Guid.TryParse(actor.UserId, out var selfId) && selfId == user.Id &&
            admin.IsSuperAdmin && role != ScforgeAdminRoles.Super)
        {
            throw new ScforgeRuleException("不能撤销自己的超级管理员身份");
        }

        var record = await db.ScforgeAdmins.FirstOrDefaultAsync(a => a.UserId == user.Id, cancellationToken);
        if (record is null)
        {
            record = new ScforgeAdmin
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                UserName = user.Username,
                Avatar = user.Avatar,
                GrantedBy = admin.DisplayName,
            };
            db.ScforgeAdmins.Add(record);
        }
        else
        {
            if (record.IsSuper && role != ScforgeAdminRoles.Super &&
                await db.ScforgeAdmins.CountAsync(a => a.Role == ScforgeAdminRoles.Super, cancellationToken) <= 1)
            {
                throw new ScforgeRuleException("至少要保留一位超级管理员");
            }

            record.UserName = user.Username;
            record.Avatar = user.Avatar;
            record.GrantedBy = admin.DisplayName;
        }

        record.Role = role;
        record.Permissions = ScforgePermissionSet.From(permissions).Serialize();
        record.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SCForge：{Admin} 把 {User} 设为 {Role}", admin.DisplayName, user.Username, role);

        return ToDto(record);
    }

    /// <summary>撤销一名管理员（仅超管）。</summary>
    public async Task DeleteAdminAsync(
        Guid adminId,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        admin.RequireSuper();

        var record = await db.ScforgeAdmins.FirstOrDefaultAsync(a => a.Id == adminId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "该管理员记录不存在（配置白名单里的超管无法在这里撤销）");

        if (Guid.TryParse(actor.UserId, out var selfId) && selfId == record.UserId)
        {
            throw new ScforgeRuleException("不能撤销自己的管理员权限");
        }

        if (record.IsSuper &&
            await db.ScforgeAdmins.CountAsync(a => a.Role == ScforgeAdminRoles.Super, cancellationToken) <= 1)
        {
            throw new ScforgeRuleException("至少要保留一位超级管理员");
        }

        db.ScforgeAdmins.Remove(record);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SCForge：{Admin} 撤销了 {User} 的管理员权限", admin.DisplayName, record.UserName);
    }

    private static ScforgeAdminDto ToDto(ScforgeAdmin record) => new()
    {
        Id = record.Id.ToString(),
        UserId = record.UserId.ToString(),
        Username = record.UserName,
        Avatar = record.Avatar,
        Role = record.Role,
        IsSuperAdmin = record.IsSuper,
        Permissions = [.. record.EffectivePermissions],
        GrantedBy = record.GrantedBy,
        FromConfig = record.FromConfig,
        CreatedAt = record.CreatedAt,
    };
}
