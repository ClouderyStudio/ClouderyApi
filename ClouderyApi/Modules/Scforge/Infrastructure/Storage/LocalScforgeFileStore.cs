using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Domain;

namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// 本机磁盘实现：插件包放在 <c>&lt;root&gt;/packages/</c>（私有，只经下载接口流出），
/// 图片放在 <c>&lt;root&gt;/public/images/</c>（唯一被 /scforge/uploads 静态托管的部分）。
/// </summary>
public sealed class LocalScforgeFileStore : IScforgeFileStore
{
    private readonly ScforgeOptions _options;
    private readonly ILogger<LocalScforgeFileStore> _logger;

    public LocalScforgeFileStore(ScforgeOptions options, string root, ILogger<LocalScforgeFileStore> logger)
    {
        _options = options;
        Root = root;
        _logger = logger;
        Directory.CreateDirectory(Path.Combine(Root, ScforgeUploadPaths.PackagesDirectory));
        Directory.CreateDirectory(Path.Combine(Root, ScforgeUploadPaths.PublicImagesDirectory.Replace('/', Path.DirectorySeparatorChar)));
    }

    /// <summary>本机上传根目录（静态文件中间件复用）。</summary>
    public string Root { get; }

    public string Provider => "local";

    public bool IsRemote => false;

    public async Task<ScforgeStoredPackage> SavePackageAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var extension = ScforgeFilePolicy.ValidatePackage(file, _options.MaxPackageBytes);
        var bytes = await ScforgeFilePolicy.ReadBytesAsync(file, _options.MaxPackageBytes, cancellationToken);
        var key = ScforgeFilePolicy.PackageKey(bytes, extension);
        var target = ResolvePath(key);

        // 内容寻址：同一份包重复上传（作者重试、多版本复用）不重复占用磁盘。
        if (!File.Exists(target))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, bytes, cancellationToken);
        }

        using var probe = new MemoryStream(bytes, writable: false);
        try
        {
            // 损坏的压缩包在这里就被拒绝，避免发布之后才暴露「无法安装」。
            ScforgePackageInspector.EnsureReadable(probe, extension);
        }
        catch
        {
            if (!File.Exists(target) || bytes.Length > 0) TryDelete(key);
            throw;
        }

        var manifest = ScforgePackageInspector.ReadManifest(probe, extension, _logger);
        return new ScforgeStoredPackage(key, Path.GetFileName(file.FileName), bytes.Length, ScforgeFilePolicy.Sha256(bytes), extension, manifest);
    }

    public async Task<string> SaveImageAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var webp = ScforgeFilePolicy.EncodeImage(file, _options.MaxImageBytes, _logger);
        var fileName = ScforgeFilePolicy.ImageFileName();
        var target = ResolvePath($"{ScforgeUploadPaths.PublicImagesDirectory}/{fileName}");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllBytesAsync(target, webp, cancellationToken);
        return ScforgeUploadPaths.PublicImageUrl(fileName);
    }

    public Task<ScforgeOpenedPackage?> OpenPackageAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = ResolvePathOrNull(storageKey);
        if (path is null || !File.Exists(path)) return Task.FromResult<ScforgeOpenedPackage?>(null);

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, useAsync: true);
        return Task.FromResult<ScforgeOpenedPackage?>(new ScforgeOpenedPackage(stream, stream.Length));
    }

    public void TryDelete(string? storageKey)
    {
        var path = ResolvePathOrNull(storageKey);
        if (path is null) return;
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除本地上传对象失败，已跳过：{Key}", storageKey);
        }
    }

    public void TryDeleteByUrl(string? publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl)) return;
        var prefix = ScforgeUploadPaths.RequestPath + "/";
        if (!publicUrl.StartsWith(prefix, StringComparison.Ordinal)) return;
        // 公开 URL 与本地路径的对应关系：/scforge/uploads/<rel> -> public/<rel>
        TryDelete("public/" + publicUrl[prefix.Length..]);
    }

    /// <summary>把存储键解析成本机绝对路径，并阻断越权 / 路径穿越。</summary>
    private string? ResolvePathOrNull(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return null;

        var relative = storageKey.Replace('\\', '/').TrimStart('/');
        if (relative.Contains("..", StringComparison.Ordinal) || Path.IsPathRooted(storageKey)) return null;

        var full = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        var rootFull = Path.GetFullPath(Root);
        return full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    private string ResolvePath(string storageKey) =>
        Path.Combine(Root, ScforgeUploadPaths.NormalizeKey(storageKey).Replace('/', Path.DirectorySeparatorChar));
}
