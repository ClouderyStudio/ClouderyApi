using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Application.Mapping;
using ClouderyApi.Modules.Scforge.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>一次下载所需的全部信息。</summary>
public sealed record ScforgeDownload(Stream Stream, string FileName, long Length);

/// <summary>浏览页查询参数（已由控制器归一化）。</summary>
public sealed record ScforgeSearchQuery(
    string? Term,
    string? Category,
    string? Tag,
    string? GameVersion,
    string Sort,
    int Page,
    int PageSize,
    string? Kind = null);

/// <summary>
/// 插件的用例编排：浏览 / 详情 / 发布 / 编辑 / 版本维护 / 删除 / 下载。
///
/// **先审后发**：新提交、作者编辑过的资料、作者改动过的版本都回到 Pending，
/// 只有管理员（review 权限）通过后公众才可见；审核判定在 ScforgeAdminAppService。
/// 权限：发布类操作只属于作者；拥有 content 权限的管理员可以编辑 / 删除任意插件。
/// </summary>
public sealed class ScforgePluginAppService(
    IScforgeDbContext db,
    IScforgeFileStore files,
    ScforgeVoteAppService votes,
    ScforgeGameVersionAppService gameVersions,
    ScforgeAccessAppService access,
    ILogger<ScforgePluginAppService> logger)
{
    private const int MaxDownloadNameLength = 255;

    /* ============================ 浏览 ============================ */

    public async Task<ScforgeSearchResultDto> SearchAsync(
        ScforgeSearchQuery query,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        var pageSize = Math.Clamp(query.PageSize <= 0 ? 24 : query.PageSize, 1, 60);
        var page = Math.Max(query.Page, 1);
        var term = Sanitize(query.Term);

        // 受支持的游戏版本以数据库为准（超管可在后台添加），一次取出供过滤与聚合共用。
        var supportedVersions = await gameVersions.KeysAsync(cancellationToken);

        var source = PublicPlugins();

        if (!string.IsNullOrEmpty(term))
        {
            var pattern = $"%{term}%";
            source = source.Where(p =>
                EF.Functions.Like(p.Name, pattern) ||
                EF.Functions.Like(p.Summary, pattern) ||
                EF.Functions.Like(p.AuthorName, pattern) ||
                EF.Functions.Like(p.Slug, pattern) ||
                EF.Functions.Like(p.TagsText, pattern));
        }

        if (!string.IsNullOrEmpty(query.Category) && ScforgeCatalog.IsCategory(query.Category))
        {
            source = source.Where(p => p.Category == query.Category);
        }

        if (!string.IsNullOrEmpty(query.Tag) && ScforgeCatalog.Tags.ContainsKey(query.Tag))
        {
            // TagsText 形如 |survival|pvp|，取值来自受控目录，不含 LIKE 通配符。
            source = source.Where(p => EF.Functions.Like(p.TagsText, $"%|{query.Tag}|%"));
        }

        if (!string.IsNullOrEmpty(query.GameVersion) && supportedVersions.Contains(query.GameVersion, StringComparer.Ordinal))
        {
            source = source.Where(p => EF.Functions.Like(p.GameVersionsText, $"%|{query.GameVersion}|%"));
        }

        // 插件 / 模组是两块独立的板：非法取值忽略而不是报错，避免旧链接直接打不开。
        if (ScforgeCatalog.IsKind(query.Kind))
        {
            source = source.Where(p => p.Kind == query.Kind);
        }

        var total = await source.CountAsync(cancellationToken);
        var totalPages = total == 0 ? 1 : (int)Math.Ceiling(total / (double)pageSize);
        if (page > totalPages) page = totalPages;

        var plugins = await ApplySort(source, query.Sort, term)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new ScforgeSearchResultDto
        {
            Items = await ProjectSummariesAsync(plugins, actor, cancellationToken),
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = totalPages,
            Facets = await BuildFacetsAsync(supportedVersions, cancellationToken),
        };
    }

    /// <summary>精选位：显式标记的排前面，不足时按净评分补齐（冷启动也不会是空板块）。</summary>
    public async Task<List<ScforgeAddonSummaryDto>> FeaturedAsync(
        int limit,
        string? kind,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit <= 0 ? 6 : limit, 1, 24);
        var source = PublicPlugins();
        if (ScforgeCatalog.IsKind(kind)) source = source.Where(p => p.Kind == kind);

        var featured = await source
            .Where(p => p.Featured)
            .OrderByDescending(p => p.Upvotes - p.Downvotes)
            .ThenByDescending(p => p.Downloads)
            .Take(take)
            .ToListAsync(cancellationToken);

        if (featured.Count >= take) return await ProjectSummariesAsync(featured, actor, cancellationToken);

        var featuredIds = featured.Select(p => p.Id).ToList();
        var fill = await source
            .Where(p => !featuredIds.Contains(p.Id))
            .OrderByDescending(p => p.Upvotes - p.Downvotes)
            .ThenByDescending(p => p.Downloads)
            .ThenByDescending(p => p.UpdatedAt)
            .Take(take - featured.Count)
            .ToListAsync(cancellationToken);

        featured.AddRange(fill);
        return await ProjectSummariesAsync(featured, actor, cancellationToken);
    }

    public async Task<List<ScforgeAddonSummaryDto>> RecentAsync(
        int limit,
        string? kind,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(limit <= 0 ? 8 : limit, 1, 24);
        var source = PublicPlugins();
        if (ScforgeCatalog.IsKind(kind)) source = source.Where(p => p.Kind == kind);

        var plugins = await source
            .OrderByDescending(p => p.UpdatedAt)
            .ThenByDescending(p => p.Downloads)
            .Take(take)
            .ToListAsync(cancellationToken);

        return await ProjectSummariesAsync(plugins, actor, cancellationToken);
    }

    /// <summary>
    /// 详情：idOrSlug 接受 GUID 或 slug。
    /// 公开可见 = 插件已通过审核且至少有一个已通过审核的版本；
    /// 作者与管理员可预览未通过的内容（管理员需要它来做审核）。
    ///
    /// 隐私插件**不返回 404**，而是返回脱敏外壳 + <c>hasAccess=false</c>：
    /// 作者本就要靠 slug 把链接分享出去，「存在但你没权限」不是需要隐藏的信息。
    /// 真正的内容（描述 / readme / 截图 / 版本）在无权时一律不下发。
    /// </summary>
    public async Task<ScforgeAddonDetailDto> GetAsync(
        string idOrSlug,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        var plugin = await FindAsync(idOrSlug, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");

        var isAuthor = actor.IsAuthorOf(plugin.AuthorId);
        var privileged = isAuthor || admin.IsAdmin;

        var versions = await db.ScforgeVersions
            .AsNoTracking()
            .Where(v => v.PluginId == plugin.Id)
            .OrderByDescending(v => v.PublishedAt)
            .ThenByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

        if (!privileged)
        {
            // 未通过审核对公众一律按「不存在」处理，避免探测未发布内容。
            if (plugin.Status != ScforgeContentStatus.Published)
            {
                throw new ScforgeApiException(404, "插件不存在或已被删除");
            }

            versions = versions.Where(v => v.Status == ScforgeContentStatus.Published).ToList();
            if (versions.Count == 0) throw new ScforgeApiException(404, "插件不存在或已被删除");
        }

        var unlocked = access.IsUnlocked(plugin, accessToken);
        var hasAccess = await access.CanAccessAsync(plugin, actor, admin, unlocked, cancellationToken);

        // 无权访问时把版本列表也清掉再交给映射器（它会把正文一并脱敏）。
        var visibleVersions = hasAccess ? versions : [];

        var myVote = hasAccess
            ? await GetVoteAsync(actor.UserId, ScforgeVoteTargets.Plugin, plugin.Id, cancellationToken)
            : 0;

        return ScforgeMapper.ToDetail(
            plugin, visibleVersions, myVote, isAuthor, admin.CanReview, admin.CanManageContent, hasAccess, unlocked);
    }

    /// <summary>当前用户发布的插件（含待审核与已驳回）。</summary>
    public async Task<List<ScforgeAddonSummaryDto>> MineAsync(
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.RequireUserId();

        var plugins = await db.ScforgePlugins
            .AsNoTracking()
            .Where(p => p.AuthorId == userId)
            .OrderByDescending(p => p.UpdatedAt)
            .ToListAsync(cancellationToken);

        return await ProjectSummariesAsync(plugins, actor, cancellationToken);
    }

    public async Task<ScforgeMineSummaryDto> MineSummaryAsync(
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.RequireUserId();

        var rows = await db.ScforgePlugins
            .AsNoTracking()
            .Where(p => p.AuthorId == userId)
            .Select(p => new { p.Downloads, p.Upvotes, p.CommentCount, p.Status })
            .ToListAsync(cancellationToken);

        return new ScforgeMineSummaryDto
        {
            Addons = rows.Count,
            Downloads = rows.Sum(r => r.Downloads),
            Upvotes = rows.Sum(r => r.Upvotes),
            Comments = rows.Sum(r => r.CommentCount),
            Pending = rows.Count(r => r.Status == ScforgeContentStatus.Pending),
            Published = rows.Count(r => r.Status == ScforgeContentStatus.Published),
            Rejected = rows.Count(r => r.Status == ScforgeContentStatus.Rejected),
        };
    }

    /* ============================ 发布 ============================ */

    /// <summary>发布新插件：插件与首个版本都进入待审核。</summary>
    public async Task<ScforgeAddonDetailDto> CreateAsync(
        ScforgeAddonCreateForm form,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        var userId = actor.RequireUserId();
        if (form.Package is null) throw new ScforgeRuleException("请上传插件包");

        EnsureCategory(form.Category);
        var supportedVersions = await gameVersions.KeysAsync(cancellationToken);
        EnsureGameVersion(form.GameVersion, supportedVersions);
        EnsureUrl(form.SourceUrl, "源码地址");
        EnsureUrl(form.IssuesUrl, "问题反馈地址");
        EnsureUrl(form.LicenseUrl, "许可证链接");
        EnsureUrl(form.DonationUrl, "赞助链接");
        EnsureUrl(form.DiscordUrl, "Discord / 群组链接");

        // 资源类型决定包格式：插件是 .dll，模组是 .netmod（会下发到客户端）。
        var kind = ResolveKind(form.Kind);
        EnsurePackageMatchesKind(kind, form.Package.FileName);

        // 先落包再解析清单：manifest.json 可以补全作者没填的名称与版本号。
        var stored = await files.SavePackageAsync(form.Package, cancellationToken);
        // 清单由存储在落盘时顺带解析（本地读盘 / OSS 读的是刚上传的内存副本），不再二次读取远端对象。
        var manifest = stored.Manifest;

        var name = FirstNonEmpty(form.Name, manifest?.Name);
        var versionText = FirstNonEmpty(form.Version, manifest?.Version);

        EnsureText(name, "插件名称", ScforgeCatalog.MaxNameLength, required: true);
        var summary = (form.Summary ?? string.Empty).Trim();
        var description = (form.Description ?? string.Empty).Trim();
        EnsureText(summary, "一句话简介", ScforgeCatalog.MaxSummaryLength, required: true);
        EnsureText(description, "详细描述", ScforgeCatalog.MaxDescriptionLength, required: true);
        EnsureText(form.Readme, "README", ScforgeCatalog.MaxReadmeLength, required: false);
        EnsureText(versionText, "版本号", ScforgeCatalog.MaxVersionLength, required: true);

        var tags = NormalizeTags(form.Tags);
        var slug = ScforgeCatalog.NormalizeSlug(FirstNonEmpty(form.Slug, name));
        if (slug.Length < 2) throw new ScforgeRuleException("访问标识至少需要 2 个字母或数字");

        var exists = await db.ScforgePlugins.AnyAsync(p => p.Slug == slug, cancellationToken);
        if (exists) throw new ScforgeApiException(409, $"访问标识「{slug}」已被占用，请换一个");

        var now = DateTime.UtcNow;
        var plugin = new ScforgePlugin
        {
            Id = Guid.NewGuid(),
            Slug = slug,
            Kind = kind,
            Name = name!,
            Summary = summary,
            Description = description,
            Readme = (form.Readme ?? string.Empty).Trim(),
            Category = form.Category!,
            Tags = tags,
            TagsText = Pipeline(tags),
            GameVersion = form.GameVersion!,
            SourceUrl = Trimmed(form.SourceUrl),
            IssuesUrl = Trimmed(form.IssuesUrl),
            License = Trimmed(form.License),
            LicenseUrl = Trimmed(form.LicenseUrl),
            DonationUrl = Trimmed(form.DonationUrl),
            DiscordUrl = Trimmed(form.DiscordUrl),
            AuthorId = userId,
            AuthorName = actor.DisplayName,
            AuthorAvatar = Trimmed(actor.Avatar),
            Status = ScforgeContentStatus.Pending,
            PublishedAt = now,
            UpdatedAt = now,
        };

        if (form.Icon is not null)
        {
            plugin.IconUrl = await files.SaveImageAsync(form.Icon, cancellationToken);
        }

        plugin.Gallery = await SaveGalleryAsync(form.Gallery, cancellationToken);

        // 隐私设置：缺省公开。口令现算哈希，明文不落库。
        // 走访问服务而不是就地赋值 —— 创建与编辑必须共用同一套规则，否则两边会漂移。
        // 此刻插件还没入库，名单表里不会有它的行，ApplyModeAsync 的清理逻辑天然空转。
        await access.ApplyModeAsync(
            plugin,
            ScforgeAccessMode.Normalize(form.AccessMode),
            form.AccessPassword,
            form.AccessHint,
            passwordProvided: !string.IsNullOrEmpty(form.AccessPassword),
            cancellationToken);

        var version = BuildVersion(
            plugin.Id, stored, versionText!, form.Channel, form.Changelog,
            form.GameVersions, form.Dependencies, plugin.GameVersion, supportedVersions);
        plugin.GameVersionsText = Pipeline(version.GameVersions);
        plugin.Versions.Add(version);

        db.ScforgePlugins.Add(plugin);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("SCForge：{Author} 提交插件 {Slug}（{Version}），等待审核", actor.DisplayName, slug, version.Version);

        return ScforgeMapper.ToDetail(plugin, [version], 0, true, false, false);
    }

    /// <summary>追加版本：新版本进入待审核，已发布的版本不受影响。</summary>
    public async Task<ScforgeVersionDto> AddVersionAsync(
        Guid pluginId,
        ScforgeVersionCreateForm form,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        actor.RequireUserId();
        if (form.Package is null) throw new ScforgeRuleException("请上传插件包");

        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");
        if (!actor.IsAuthorOf(plugin.AuthorId))
        {
            throw new ScforgeApiException(403, "只有作者可以发布新版本");
        }

        EnsureText(form.Version, "版本号", ScforgeCatalog.MaxVersionLength, required: true);
        var versionText = form.Version!.Trim();

        var duplicate = await db.ScforgeVersions
            .AnyAsync(v => v.PluginId == pluginId && v.Version == versionText, cancellationToken);
        if (duplicate) throw new ScforgeApiException(409, $"版本号「{versionText}」已存在");

        EnsurePackageMatchesKind(plugin.Kind, form.Package.FileName);
        var stored = await files.SavePackageAsync(form.Package, cancellationToken);
        var version = BuildVersion(
            pluginId, stored, versionText, form.Channel, form.Changelog,
            form.GameVersions, form.Dependencies, form.GameVersion ?? plugin.GameVersion,
            await gameVersions.KeysAsync(cancellationToken));

        db.ScforgeVersions.Add(version);
        plugin.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await RefreshGameVersionsTextAsync(plugin, cancellationToken);

        logger.LogInformation("SCForge：{Author} 为 {Slug} 提交版本 {Version}，等待审核", actor.DisplayName, plugin.Slug, version.Version);

        return ScforgeMapper.ToVersion(version, plugin);
    }

    /// <summary>
    /// 编辑插件资料（作者，或有 content 权限的管理员）。
    /// 任何编辑都会重新进入待审核：审核通过前不再对公众可见。
    /// </summary>
    public async Task<ScforgeAddonDetailDto> UpdateAsync(
        Guid pluginId,
        ScforgeAddonEditForm form,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        actor.RequireUserId();

        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");

        if (!actor.IsAuthorOf(plugin.AuthorId) && !admin.CanManageContent)
        {
            throw new ScforgeApiException(403, "只有作者（或有内容管理权限的管理员）可以修改插件资料");
        }

        EnsureUrl(form.SourceUrl, "源码地址");
        EnsureUrl(form.IssuesUrl, "问题反馈地址");
        EnsureUrl(form.LicenseUrl, "许可证链接");
        EnsureUrl(form.DonationUrl, "赞助链接");
        EnsureUrl(form.DiscordUrl, "Discord / 群组链接");

        if (form.Summary is not null)
        {
            var summary = form.Summary.Trim();
            EnsureText(summary, "一句话简介", ScforgeCatalog.MaxSummaryLength, required: true);
            plugin.Summary = summary;
        }

        if (form.Description is not null)
        {
            var description = form.Description.Trim();
            EnsureText(description, "详细描述", ScforgeCatalog.MaxDescriptionLength, required: true);
            plugin.Description = description;
        }

        if (form.Readme is not null)
        {
            EnsureText(form.Readme, "README", ScforgeCatalog.MaxReadmeLength, required: false);
            plugin.Readme = form.Readme.Trim();
        }

        if (form.Category is not null)
        {
            EnsureCategory(form.Category);
            plugin.Category = form.Category;
        }

        if (form.GameVersion is not null)
        {
            EnsureGameVersion(form.GameVersion, await gameVersions.KeysAsync(cancellationToken));
            plugin.GameVersion = form.GameVersion;
        }

        if (form.Tags is not null)
        {
            var tags = NormalizeTags(form.Tags);
            plugin.Tags = tags;
            plugin.TagsText = Pipeline(tags);
        }

        if (form.SourceUrl is not null) plugin.SourceUrl = Trimmed(form.SourceUrl);
        if (form.IssuesUrl is not null) plugin.IssuesUrl = Trimmed(form.IssuesUrl);
        if (form.License is not null) plugin.License = Trimmed(form.License);
        if (form.LicenseUrl is not null) plugin.LicenseUrl = Trimmed(form.LicenseUrl);
        if (form.DonationUrl is not null) plugin.DonationUrl = Trimmed(form.DonationUrl);
        if (form.DiscordUrl is not null) plugin.DiscordUrl = Trimmed(form.DiscordUrl);

        // 图片：显式清空优先，其次替换；旧对象在替换成功后删除。
        if (form.ClearIcon == true)
        {
            files.TryDeleteByUrl(plugin.IconUrl);
            plugin.IconUrl = null;
        }
        else if (form.Icon is not null)
        {
            var previous = plugin.IconUrl;
            plugin.IconUrl = await files.SaveImageAsync(form.Icon, cancellationToken);
            files.TryDeleteByUrl(previous);
        }

        if (form.ClearGallery == true)
        {
            foreach (var image in plugin.Gallery) files.TryDeleteByUrl(image);
            plugin.Gallery = [];
        }
        else if (form.Gallery is { Count: > 0 })
        {
            var saved = await SaveGalleryAsync(form.Gallery, cancellationToken);
            foreach (var image in plugin.Gallery) files.TryDeleteByUrl(image);
            plugin.Gallery = saved;
        }

        // ---- 隐私访问 ----
        // 与内容编辑的关键区别：**改隐私不触发重新审核**。
        // 审核管的是「这个插件能不能给公众看」，而隐私是作者对访问范围的控制。
        // 若一并 MarkPending，一个已发布的插件仅仅换个口令就会当场从公开目录消失、
        // 老链接全部失效 —— 那是纯粹的误伤。
        var privacyChanged = form.AccessMode is not null
                             || form.AccessPassword is not null
                             || form.AccessHint is not null
                             || form.AccessGrantUserIds is not null;

        if (form.AccessMode is not null || form.AccessPassword is not null || form.AccessHint is not null)
        {
            var target = ScforgeAccessMode.Normalize(
                form.AccessMode ?? plugin.AccessMode);

            if (form.AccessHint is not null)
            {
                EnsureText(form.AccessHint, "访问说明", ScforgeCatalog.MaxAccessHintLength, required: false);
            }

            await access.ApplyModeAsync(
                plugin,
                target,
                form.AccessPassword,
                form.AccessHint,
                passwordProvided: !string.IsNullOrEmpty(form.AccessPassword),
                cancellationToken);
        }

        // 白名单整体替换。必须在模式切换之后：切到非白名单模式时名单会被清空，
        // 此时若再把名单写回去就成了「看不见但仍生效」的授权。
        if (form.AccessGrantUserIds is not null && plugin.AccessMode == ScforgeAccessMode.Whitelist)
        {
            await access.ReplaceGrantsAsync(pluginId, form.AccessGrantUserIds, actor, admin, cancellationToken);
        }

        // 只有内容真的变了才重新审核：隐私调整不参与。
        if (!privacyChanged) MarkPending(plugin);

        await db.SaveChangesAsync(cancellationToken);
        await RefreshGameVersionsTextAsync(plugin, cancellationToken);

        logger.LogInformation(
            privacyChanged
                ? "SCForge：{Actor} 调整了插件 {Slug} 的隐私设置（{Mode}），不触发重新审核"
                : "SCForge：{Actor} 编辑了插件 {Slug}，重新进入待审核",
            actor.DisplayName, plugin.Slug, plugin.AccessMode);

        return await BuildDetailAsync(plugin, actor, admin, cancellationToken);
    }

    /// <summary>删除插件（连同其版本、评论与投票）：作者，或有 content 权限的管理员。</summary>
    public async Task DeleteAsync(
        Guid pluginId,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        actor.RequireUserId();

        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");
        if (!actor.IsAuthorOf(plugin.AuthorId) && !admin.CanManageContent)
        {
            throw new ScforgeApiException(403, "只有作者（或有内容管理权限的管理员）可以删除插件");
        }

        var storageKeys = await db.ScforgeVersions
            .Where(v => v.PluginId == pluginId)
            .Select(v => v.StorageKey)
            .ToListAsync(cancellationToken);

        var commentIds = await db.ScforgeComments
            .Where(c => c.PluginId == pluginId)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        // 评论与投票没有配置级联外键（评论的父指针是自引用，级联路径有歧义），显式清理。
        if (commentIds.Count > 0)
        {
            await db.ScforgeComments.Where(c => c.PluginId == pluginId).ExecuteDeleteAsync(cancellationToken);
            await db.ScforgeVotes
                .Where(v => v.TargetType == ScforgeVoteTargets.Comment && commentIds.Contains(v.TargetId))
                .ExecuteDeleteAsync(cancellationToken);
        }

        await db.ScforgeVotes
            .Where(v => v.TargetType == ScforgeVoteTargets.Plugin && v.TargetId == pluginId)
            .ExecuteDeleteAsync(cancellationToken);

        // 授权名单走级联删除即可（外键已配 Cascade），这里不重复清理。

        db.ScforgePlugins.Remove(plugin);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var storageKey in storageKeys)
        {
            await DeleteFileIfUnusedAsync(storageKey, cancellationToken);
        }

        files.TryDeleteByUrl(plugin.IconUrl);
        foreach (var image in plugin.Gallery) files.TryDeleteByUrl(image);

        logger.LogInformation("SCForge：{Actor} 删除了插件 {Slug}", actor.DisplayName, plugin.Slug);
    }

    /// <summary>作者把被驳回的插件重新提交审核（改过内容时编辑本身也会触发）。</summary>
    public async Task<ScforgeAddonDetailDto> ResubmitAsync(
        Guid pluginId,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken = default)
    {
        actor.RequireUserId();

        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == pluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");
        if (!actor.IsAuthorOf(plugin.AuthorId)) throw new ScforgeApiException(403, "只有作者可以提交审核");
        if (plugin.Status == ScforgeContentStatus.Published) throw new ScforgeRuleException("插件已发布，无需重新提交审核");

        MarkPending(plugin);
        plugin.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return await BuildDetailAsync(plugin, actor, admin, cancellationToken);
    }

    /* ============================ 版本维护 ============================ */

    /// <summary>编辑版本元数据（仅作者）：重新进入待审核。</summary>
    public async Task<ScforgeVersionDto> UpdateVersionAsync(
        Guid versionId,
        ScforgeVersionEditIn body,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        actor.RequireUserId();

        var version = await db.ScforgeVersions.FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken)
                      ?? throw new ScforgeApiException(404, "版本不存在");
        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == version.PluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");
        if (!actor.IsAuthorOf(plugin.AuthorId)) throw new ScforgeApiException(403, "只有作者可以修改版本");

        if (body.Channel is not null)
        {
            var channel = body.Channel.Trim().ToLowerInvariant();
            if (!ScforgeCatalog.IsChannel(channel)) throw new ScforgeRuleException("发布渠道只能是 release / beta / alpha");
            version.Channel = channel;
        }

        if (body.Changelog is not null)
        {
            EnsureText(body.Changelog, "更新日志", 20_000, required: false);
            version.Changelog = body.Changelog.Trim();
        }

        if (body.GameVersions is not null || body.GameVersion is not null)
        {
            var game = NormalizeGameVersions(
                body.GameVersions,
                body.GameVersion ?? version.GameVersion,
                await gameVersions.KeysAsync(cancellationToken));
            version.GameVersion = game[0];
            version.GameVersions = game;
        }

        if (body.Dependencies is not null)
        {
            version.Dependencies = NormalizeDependencies(body.Dependencies);
        }

        MarkPending(version);
        plugin.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        await RefreshGameVersionsTextAsync(plugin, cancellationToken);

        logger.LogInformation("SCForge：{Actor} 修改了 {Slug} 的版本 {Version}，重新进入待审核", actor.DisplayName, plugin.Slug, version.Version);

        return ScforgeMapper.ToVersion(version, plugin);
    }

    /// <summary>替换版本的插件包文件（仅作者）：重新进入待审核。</summary>
    public async Task<ScforgeVersionDto> ReplaceVersionFileAsync(
        Guid versionId,
        ScforgeVersionFileForm form,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        actor.RequireUserId();
        if (form.Package is null) throw new ScforgeRuleException("请上传插件包");

        var version = await db.ScforgeVersions.FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken)
                      ?? throw new ScforgeApiException(404, "版本不存在");
        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == version.PluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");
        if (!actor.IsAuthorOf(plugin.AuthorId)) throw new ScforgeApiException(403, "只有作者可以替换版本文件");

        EnsurePackageMatchesKind(plugin.Kind, form.Package.FileName);
        var stored = await files.SavePackageAsync(form.Package, cancellationToken);
        var previous = version.StorageKey;

        version.FileName = stored.FileName;
        version.StorageKey = stored.StorageKey;
        version.FileSize = stored.Size;
        version.Sha256 = stored.Sha256;
        MarkPending(version);
        plugin.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await DeleteFileIfUnusedAsync(previous, cancellationToken);

        logger.LogInformation("SCForge：{Actor} 替换了 {Slug} 版本 {Version} 的文件，重新进入待审核", actor.DisplayName, plugin.Slug, version.Version);

        return ScforgeMapper.ToVersion(version, plugin);
    }

    /// <summary>删除一个版本（仅作者）。删除是下架动作，不需要审核。</summary>
    public async Task DeleteVersionAsync(
        Guid versionId,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        actor.RequireUserId();

        var version = await db.ScforgeVersions.FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken)
                      ?? throw new ScforgeApiException(404, "版本不存在");
        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == version.PluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");
        if (!actor.IsAuthorOf(plugin.AuthorId)) throw new ScforgeApiException(403, "只有作者可以删除版本");

        var remaining = await db.ScforgeVersions.CountAsync(v => v.PluginId == plugin.Id, cancellationToken);
        if (remaining <= 1) throw new ScforgeRuleException("至少要保留一个版本；如需下架整个插件请删除插件");

        var storageKey = version.StorageKey;
        db.ScforgeVersions.Remove(version);
        plugin.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        await DeleteFileIfUnusedAsync(storageKey, cancellationToken);
        await RefreshGameVersionsTextAsync(plugin, cancellationToken);
    }

    /// <summary>作者把被驳回的版本重新提交审核。</summary>
    public async Task<ScforgeVersionDto> ResubmitVersionAsync(
        Guid versionId,
        ScforgeActor actor,
        CancellationToken cancellationToken = default)
    {
        actor.RequireUserId();

        var version = await db.ScforgeVersions.FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken)
                      ?? throw new ScforgeApiException(404, "版本不存在");
        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == version.PluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");
        if (!actor.IsAuthorOf(plugin.AuthorId)) throw new ScforgeApiException(403, "只有作者可以提交审核");
        if (version.Status == ScforgeContentStatus.Published) throw new ScforgeRuleException("该版本已通过审核，无需重新提交");

        MarkPending(version);
        plugin.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return ScforgeMapper.ToVersion(version, plugin);
    }

    /* ============================ 下载 ============================ */

    public async Task<ScforgeDownload> OpenDownloadAsync(
        Guid versionId,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        string? accessToken = null,
        CancellationToken cancellationToken = default)
    {
        var version = await db.ScforgeVersions.FirstOrDefaultAsync(v => v.Id == versionId, cancellationToken)
                      ?? throw new ScforgeApiException(404, "版本不存在");
        var plugin = await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == version.PluginId, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件不存在或已被删除");

        var privileged = actor.IsAuthorOf(plugin.AuthorId) || admin.IsAdmin;
        if (!privileged &&
            (plugin.Status != ScforgeContentStatus.Published || version.Status != ScforgeContentStatus.Published))
        {
            throw new ScforgeApiException(404, "版本不存在或尚未通过审核");
        }

        // 隐私判定必须在这里再走一遍：详情页脱敏不代表直链下载也被挡住，
        // 拿到 versionId 的人可以直接打 /versions/{id}/download 绕过界面。
        if (!await access.CanAccessAsync(plugin, actor, admin, access.IsUnlocked(plugin, accessToken), cancellationToken))
        {
            ScforgeAccessAppService.Deny(plugin);
        }

        var opened = await files.OpenPackageAsync(version.StorageKey, cancellationToken)
                     ?? throw new ScforgeApiException(404, "插件包已丢失，请联系作者重新上传");

        // 下载计数：版本与插件各记一笔，列表排序读的是插件这一列。
        version.Downloads += 1;
        plugin.Downloads += 1;
        await db.SaveChangesAsync(cancellationToken);

        var fileName = Path.GetFileName(version.FileName);
        if (fileName.Length > MaxDownloadNameLength) fileName = fileName[..MaxDownloadNameLength];
        if (!Path.HasExtension(fileName)) fileName += Path.GetExtension(version.StorageKey);
        return new ScforgeDownload(opened.Stream, fileName, opened.Length);
    }

    /* ============================ 内部工具 ============================ */

    /// <summary>
    /// 公开可见的插件：已通过审核、至少有一个已通过审核的版本，**且未设隐私**。
    ///
    /// 隐私插件一律排除：它连「存在」都不该出现在目录、搜索与分类计数里。
    /// 作者仍可通过 slug 直达详情页并分享链接，所以这不是「不存在」，只是不外露。
    /// </summary>
    private IQueryable<ScforgePlugin> PublicPlugins() =>
        db.ScforgePlugins
            .AsNoTracking()
            .Where(p => p.Status == ScforgeContentStatus.Published)
            .Where(p => p.AccessMode == ScforgeAccessMode.Public)
            .Where(p => p.Versions.Any(v => v.Status == ScforgeContentStatus.Published));

    private static void MarkPending(ScforgePlugin plugin)
    {
        plugin.Status = ScforgeContentStatus.Pending;
        plugin.ReviewNote = null;
        plugin.ReviewedAt = null;
        plugin.ReviewedBy = null;
    }

    private static void MarkPending(ScforgeVersion version)
    {
        version.Status = ScforgeContentStatus.Pending;
        version.ReviewNote = null;
        version.ReviewedAt = null;
        version.ReviewedBy = null;
    }

    private async Task<ScforgeAddonDetailDto> BuildDetailAsync(
        ScforgePlugin plugin,
        ScforgeActor actor,
        ScforgeAdminContext admin,
        CancellationToken cancellationToken)
    {
        var versions = await db.ScforgeVersions
            .AsNoTracking()
            .Where(v => v.PluginId == plugin.Id)
            .OrderByDescending(v => v.PublishedAt)
            .ThenByDescending(v => v.Version)
            .ToListAsync(cancellationToken);

        var myVote = await GetVoteAsync(actor.UserId, ScforgeVoteTargets.Plugin, plugin.Id, cancellationToken);
        return ScforgeMapper.ToDetail(
            plugin,
            versions,
            myVote,
            actor.IsAuthorOf(plugin.AuthorId),
            admin.CanReview,
            admin.CanManageContent);
    }
    /// <summary>内容寻址的文件可能被多个版本共享：确认无引用后再删。</summary>
    private async Task DeleteFileIfUnusedAsync(string? storageKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return;
        var stillUsed = await db.ScforgeVersions.AnyAsync(v => v.StorageKey == storageKey, cancellationToken);
        if (!stillUsed) files.TryDelete(storageKey);
    }

    /// <summary>按「已通过审核的版本 + 插件自身声明」重建可用于筛选的版本管线串。</summary>
    internal async Task RefreshGameVersionsTextAsync(ScforgePlugin plugin, CancellationToken cancellationToken)
    {
        var publishedVersions = await db.ScforgeVersions
            .AsNoTracking()
            .Where(v => v.PluginId == plugin.Id && v.Status == ScforgeContentStatus.Published)
            .Select(v => v.GameVersions)
            .ToListAsync(cancellationToken);

        var all = new List<string> { plugin.GameVersion };
        foreach (var list in publishedVersions) all.AddRange(list);

        var text = Pipeline(all.Distinct(StringComparer.Ordinal));
        if (text == plugin.GameVersionsText) return;

        plugin.GameVersionsText = text;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<ScforgePlugin?> FindAsync(string idOrSlug, CancellationToken cancellationToken)
    {
        var key = (idOrSlug ?? string.Empty).Trim();
        if (key.Length == 0) return null;

        return Guid.TryParse(key, out var id)
            ? await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            : await db.ScforgePlugins.FirstOrDefaultAsync(p => p.Slug == key, cancellationToken);
    }

    private static IQueryable<ScforgePlugin> ApplySort(IQueryable<ScforgePlugin> query, string? sort, string? term)
    {
        return (sort ?? "relevance").Trim().ToLowerInvariant() switch
        {
            "downloads" => query.OrderByDescending(p => p.Downloads).ThenByDescending(p => p.UpdatedAt),
            "recent" => query.OrderByDescending(p => p.UpdatedAt).ThenByDescending(p => p.Downloads),
            "name" => query.OrderBy(p => p.Name),
            // 综合：有关键词时名称精确命中优先，其余按净评分、再按下载量。
            _ => string.IsNullOrEmpty(term)
                ? query.OrderByDescending(p => p.Upvotes - p.Downvotes).ThenByDescending(p => p.Downloads)
                : query.OrderByDescending(p => p.Name == term ? 1 : 0)
                    .ThenByDescending(p => p.Upvotes - p.Downvotes)
                    .ThenByDescending(p => p.Downloads),
        };
    }

    /// <summary>批量补齐列表项的版本统计与「我的投票」，避免逐条查库。</summary>
    internal async Task<List<ScforgeAddonSummaryDto>> ProjectSummariesAsync(
        IReadOnlyList<ScforgePlugin> plugins,
        ScforgeActor actor,
        CancellationToken cancellationToken)
    {
        if (plugins.Count == 0) return [];

        var ids = plugins.Select(p => p.Id).ToList();

        var versionRows = await db.ScforgeVersions
            .AsNoTracking()
            .Where(v => ids.Contains(v.PluginId))
            .Select(v => new { v.PluginId, v.Version, v.PublishedAt, v.Status })
            .ToListAsync(cancellationToken);

        var stats = versionRows
            .GroupBy(v => v.PluginId)
            .ToDictionary(g => g.Key, g =>
            {
                var published = g
                    .Where(v => v.Status == ScforgeContentStatus.Published)
                    .OrderByDescending(v => v.PublishedAt)
                    .ThenByDescending(v => v.Version)
                    .ToList();
                var latest = published.Count > 0 ? published[0] : null;
                return new ScforgeMapper.VersionStat(g.Count(), published.Count, latest?.Version, latest?.PublishedAt);
            });

        var myVotes = await votes.LoadVotesAsync(actor.UserId, ScforgeVoteTargets.Plugin, ids, cancellationToken);

        return plugins
            .Select(p => ScforgeMapper.ToSummary(
                p,
                stats.TryGetValue(p.Id, out var stat) ? stat : new ScforgeMapper.VersionStat(0, 0, null, null),
                myVotes.TryGetValue(p.Id, out var vote) ? vote : 0))
            .ToList();
    }

    private async Task<ScforgeFacetsDto> BuildFacetsAsync(
        IReadOnlyList<string> supportedVersions,
        CancellationToken cancellationToken)
    {
        // 只取三个用于聚合的列：插件数量增长时这里仍然是顺序扫描 + 内存聚合，可控。
        var rows = await PublicPlugins()
            .Select(p => new { p.Kind, p.Category, p.Tags, p.GameVersionsText, p.GameVersion })
            .ToListAsync(cancellationToken);

        var kindCounts = rows.GroupBy(r => r.Kind).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var categoryCounts = rows.GroupBy(r => r.Category).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        var tagCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var versionCounts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            foreach (var tag in row.Tags.Distinct(StringComparer.Ordinal))
            {
                tagCounts[tag] = tagCounts.GetValueOrDefault(tag) + 1;
            }

            foreach (var version in Unpipeline(row.GameVersionsText).Append(row.GameVersion).Distinct(StringComparer.Ordinal))
            {
                versionCounts[version] = versionCounts.GetValueOrDefault(version) + 1;
            }
        }

        return new ScforgeFacetsDto
        {
            Kinds = ScforgeCatalog.Kinds
                .Select(kv => new ScforgeFacetDto { Key = kv.Key, Label = kv.Value, Count = kindCounts.GetValueOrDefault(kv.Key) })
                .ToList(),
            Categories = ScforgeCatalog.Categories
                .Select(kv => new ScforgeFacetDto { Key = kv.Key, Label = kv.Value, Count = categoryCounts.GetValueOrDefault(kv.Key) })
                .ToList(),
            Tags = ScforgeCatalog.Tags
                .Select(kv => new ScforgeFacetDto { Key = kv.Key, Label = kv.Value, Count = tagCounts.GetValueOrDefault(kv.Key) })
                .OrderByDescending(f => f.Count)
                .ThenBy(f => f.Label, StringComparer.Ordinal)
                .ToList(),
            GameVersions = supportedVersions
                .Where(v => versionCounts.ContainsKey(v))
                .ToList(),
        };
    }

    private static ScforgeVersion BuildVersion(
        Guid pluginId,
        ScforgeStoredPackage stored,
        string version,
        string? channel,
        string? changelog,
        List<string>? gameVersions,
        List<string>? dependencies,
        string fallbackGameVersion,
        IReadOnlyList<string> supportedVersions)
    {
        var game = NormalizeGameVersions(gameVersions, fallbackGameVersion, supportedVersions);
        var deps = NormalizeDependencies(dependencies);

        var resolvedChannel = string.IsNullOrWhiteSpace(channel) ? "release" : channel.Trim().ToLowerInvariant();
        if (!ScforgeCatalog.IsChannel(resolvedChannel))
        {
            throw new ScforgeRuleException("发布渠道只能是 release / beta / alpha");
        }

        EnsureText(changelog, "更新日志", 20_000, required: false);

        return new ScforgeVersion
        {
            Id = Guid.NewGuid(),
            PluginId = pluginId,
            Version = version,
            Channel = resolvedChannel,
            Changelog = (changelog ?? string.Empty).Trim(),
            FileName = stored.FileName,
            StorageKey = stored.StorageKey,
            FileSize = stored.Size,
            Sha256 = stored.Sha256,
            GameVersion = game[0],
            GameVersions = game,
            Dependencies = deps,
            Status = ScforgeContentStatus.Pending,
            PublishedAt = DateTime.UtcNow,
        };
    }

    private async Task<List<string>> SaveGalleryAsync(List<IFormFile>? gallery, CancellationToken cancellationToken)
    {
        if (gallery is null || gallery.Count == 0) return [];

        var filesToSave = gallery.Where(f => f is { Length: > 0 }).ToList();
        if (filesToSave.Count > ScforgeCatalog.MaxGallery)
        {
            throw new ScforgeRuleException($"截图最多 {ScforgeCatalog.MaxGallery} 张");
        }

        var urls = new List<string>();
        foreach (var file in filesToSave)
        {
            urls.Add(await files.SaveImageAsync(file, cancellationToken));
        }

        return urls;
    }

    internal static List<string> NormalizeTags(List<string>? tags)
    {
        if (tags is null || tags.Count == 0) return [];

        var normalized = tags
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Count > ScforgeCatalog.MaxTags)
        {
            throw new ScforgeRuleException($"标签最多选择 {ScforgeCatalog.MaxTags} 个");
        }

        var unknown = normalized.Where(t => !ScforgeCatalog.Tags.ContainsKey(t)).ToList();
        if (unknown.Count > 0)
        {
            throw new ScforgeRuleException($"不支持的标签：{string.Join("、", unknown)}");
        }

        return normalized;
    }

    internal static List<string> NormalizeGameVersions(
        List<string>? values,
        string fallback,
        IReadOnlyList<string> supportedVersions)
    {
        var list = (values ?? [])
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (list.Count == 0 && !string.IsNullOrWhiteSpace(fallback)) list.Add(fallback.Trim());
        if (list.Count == 0) throw new ScforgeRuleException("请至少选择一个兼容的游戏版本");
        if (list.Count > ScforgeCatalog.MaxGameVersionsPerRelease)
        {
            throw new ScforgeRuleException($"单个版本最多声明 {ScforgeCatalog.MaxGameVersionsPerRelease} 个兼容游戏版本");
        }

        var unknown = list.Where(v => !supportedVersions.Contains(v, StringComparer.Ordinal)).ToList();
        if (unknown.Count > 0)
        {
            throw new ScforgeRuleException($"不支持的游戏版本：{string.Join("、", unknown)}");
        }

        // 主版本放在首位：按受支持列表的顺序（新的在前）排序。
        var order = supportedVersions.ToList();
        list.Sort((a, b) => order.IndexOf(b).CompareTo(order.IndexOf(a)));
        return list;
    }

    internal static List<string> NormalizeDependencies(List<string>? values)
    {
        var list = (values ?? [])
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Select(d => d.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.Count > ScforgeCatalog.MaxDependencies)
        {
            throw new ScforgeRuleException($"依赖最多填写 {ScforgeCatalog.MaxDependencies} 项");
        }

        if (list.Any(d => d.Length > 64)) throw new ScforgeRuleException("单个依赖名称不能超过 64 个字符");
        return list;
    }

    /// <summary>归一化资源类型；缺省按插件处理，非法取值直接拒绝。</summary>
    private static string ResolveKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind)) return ScforgeCatalog.PluginKind;

        var normalized = kind.Trim().ToLowerInvariant();
        if (!ScforgeCatalog.IsKind(normalized)) throw new ScforgeRuleException("请选择有效的资源类型（插件或模组）");
        return normalized;
    }

    /// <summary>
    /// 包格式必须与资源类型一致。只在这两种合法格式之间判断归属，
    /// 其它扩展名（例如 .zip）交给文件存储给出更具体的错误。
    /// </summary>
    private static void EnsurePackageMatchesKind(string kind, string? fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension != ScforgeCatalog.PluginPackageExtension && extension != ScforgeCatalog.ModPackageExtension) return;
        if (extension == ScforgeCatalog.PackageExtensionFor(kind)) return;

        throw new ScforgeRuleException(kind == ScforgeCatalog.ModKind
            ? "模组请上传 .netmod 文件（模组会下发到客户端）"
            : "插件请上传 .dll 文件");
    }

    internal static void EnsureCategory(string? category)
    {
        if (!ScforgeCatalog.IsCategory(category)) throw new ScforgeRuleException("请选择有效的插件分类");
    }

    internal static void EnsureGameVersion(string? gameVersion, IReadOnlyList<string> supportedVersions)
    {
        if (gameVersion is null || !supportedVersions.Contains(gameVersion, StringComparer.Ordinal))
        {
            throw new ScforgeRuleException("请选择有效的游戏版本");
        }
    }

    internal static void EnsureText(string? value, string field, int max, bool required)
    {
        var text = (value ?? string.Empty).Trim();
        if (required && text.Length == 0) throw new ScforgeRuleException($"请填写{field}");
        if (text.Length > max) throw new ScforgeRuleException($"{field}不能超过 {max} 个字符");
    }

    internal static void EnsureUrl(string? value, string field)
    {
        var url = (value ?? string.Empty).Trim();
        if (url.Length == 0) return;
        if (url.Length > 512) throw new ScforgeRuleException($"{field}不能超过 512 个字符");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ScforgeRuleException($"{field}必须是 http/https 链接");
        }
    }

    private static string? FirstNonEmpty(string? first, string? second) =>
        !string.IsNullOrWhiteSpace(first) ? first.Trim() : (!string.IsNullOrWhiteSpace(second) ? second.Trim() : null);

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>把关键词里的 LIKE 通配符剥掉，避免用户输入变成模式匹配。</summary>
    private static string? Sanitize(string? value)
    {
        var text = (value ?? string.Empty).Replace("%", string.Empty).Replace("_", string.Empty).Trim();
        return text.Length == 0 ? null : (text.Length > 64 ? text[..64] : text);
    }

    internal static string Pipeline(IEnumerable<string> values) => "|" + string.Join('|', values) + "|";

    internal static IEnumerable<string> Unpipeline(string? text) =>
        string.IsNullOrEmpty(text) ? [] : text.Split('|', StringSplitOptions.RemoveEmptyEntries);

    private async Task<int> GetVoteAsync(string? userId, string targetType, Guid targetId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId)) return 0;
        var vote = await db.ScforgeVotes
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.UserId == userId && v.TargetType == targetType && v.TargetId == targetId, cancellationToken);
        return vote?.Value ?? 0;
    }
}
