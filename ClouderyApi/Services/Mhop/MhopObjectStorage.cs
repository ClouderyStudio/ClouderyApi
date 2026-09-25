using Aliyun.OSS;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// MHOP 图片对象存储抽象：本地磁盘或远端阿里云 OSS。
/// 由 `Mhop:Storage:Provider` 决定具体实现（local / oss），上传成功后返回可直接访问的 URL。
/// </summary>
public interface IMhopObjectStorage
{
    /// <summary>是否为远端对象存储。</summary>
    bool IsRemote { get; }

    /// <summary>实现名称（local / aliyun-oss），用于健康检查与日志。</summary>
    string Provider { get; }

    /// <summary>写入对象并返回可直接访问的 URL。key 形如 `avatars/xxx.webp`。</summary>
    Task<string> PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除此前由本存储写入的对象。传入入库时保存的访问地址（本地相对路径或 OSS 外链）。
    /// 地址不属于本存储时返回 false 且不做任何操作；对象本就不存在不算失败。
    /// </summary>
    Task<bool> DeleteAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>列出本存储命名空间下的全部对象，返回可直接访问的 URL，供维护工具比对孤儿文件。</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>上传路径辅助：本地静态托管根目录与请求前缀。</summary>
public static class MhopUploadPaths
{
    /// <summary>本地静态托管前缀（Program.cs 与本地存储共用）。</summary>
    public const string RequestPath = "/mhop/uploads";

    /// <summary>解析本地存储根目录；配置为空时取 `&lt;内容根&gt;/uploads`。</summary>
    public static string ResolveLocalRoot(string? configuredDir, string contentRootPath)
        => string.IsNullOrWhiteSpace(configuredDir)
            ? Path.Combine(contentRootPath, "uploads")
            : Path.GetFullPath(Path.IsPathRooted(configuredDir)
                ? configuredDir
                : Path.Combine(contentRootPath, configuredDir));

    /// <summary>规范化对象键：统一分隔符、去掉首部斜杠、拒绝路径穿越。</summary>
    public static string NormalizeKey(string key)
    {
        var normalized = (key ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0 || normalized.Contains("..", StringComparison.Ordinal))
            throw new MhopApiException(400, "非法的文件名");
        return normalized;
    }
}

/// <summary>本地磁盘存储：写入 `&lt;UploadDir&gt;/&lt;key&gt;`，URL 前缀 /mhop/uploads。</summary>
public sealed class MhopLocalObjectStorage : IMhopObjectStorage
{
    public MhopLocalObjectStorage(string root) => Root = root;

    /// <summary>本地存储根目录（供静态文件中间件复用）。</summary>
    public string Root { get; }

    public bool IsRemote => false;

    public string Provider => "local";

    public async Task<string> PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        var relative = MhopUploadPaths.NormalizeKey(key);
        var fullPath = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
        return MhopUploadPaths.RequestPath + "/" + relative;
    }

    public Task<bool> DeleteAsync(string url, CancellationToken cancellationToken = default)
    {
        var relative = ToRelativePath(url);
        if (relative is null) return Task.FromResult(false);

        var fullPath = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath)) return Task.FromResult(false);
        File.Delete(fullPath);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        var urls = new List<string>();
        if (Directory.Exists(Root))
        {
            foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(Root, file).Replace('\\', '/');
                urls.Add(MhopUploadPaths.RequestPath + "/" + relative);
            }
        }
        return Task.FromResult<IReadOnlyList<string>>(urls);
    }

    /// <summary>把 /mhop/uploads/... 形式的地址还原为相对路径；非本存储地址返回 null。</summary>
    private static string? ToRelativePath(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var marker = MhopUploadPaths.RequestPath + "/";
        var index = url.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0) return null;
        var relative = url[(index + marker.Length)..];
        return relative.Length == 0 || relative.Contains("..", StringComparison.Ordinal) ? null : relative;
    }
}

/// <summary>
/// 阿里云 OSS 存储：使用官方 SDK 的 PutObject（OSS 自有签名协议，非 S3）。
/// 对象键会加上 `Mhop:Storage:Prefix` 前缀，便于与同一 Bucket 内其它业务隔离。
/// </summary>
public sealed class MhopAliyunOssStorage : IMhopObjectStorage
{
    private readonly OssClient _client;
    private readonly MhopOssOptions _options;
    private readonly ILogger<MhopAliyunOssStorage> _logger;

    public MhopAliyunOssStorage(MhopOssOptions options, ILogger<MhopAliyunOssStorage> logger)
    {
        _options = options;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(options.Endpoint)
            || string.IsNullOrWhiteSpace(options.Bucket)
            || string.IsNullOrWhiteSpace(options.AccessKeyId)
            || string.IsNullOrWhiteSpace(options.AccessKeySecret))
        {
            throw new InvalidOperationException(
                "Mhop:Storage:Provider=oss 需要在 Mhop:Storage:Oss 中配置 Endpoint / Bucket / AccessKeyId / AccessKeySecret。");
        }

        _client = string.IsNullOrWhiteSpace(options.SecurityToken)
            ? new OssClient(options.Endpoint, options.AccessKeyId, options.AccessKeySecret)
            : new OssClient(options.Endpoint, options.AccessKeyId, options.AccessKeySecret, options.SecurityToken);
    }

    public bool IsRemote => true;

    public string Provider => "aliyun-oss";

    public async Task<string> PutAsync(string key, byte[] content, string contentType, CancellationToken cancellationToken = default)
    {
        var relative = MhopUploadPaths.NormalizeKey(key);
        var objectKey = string.IsNullOrWhiteSpace(_options.Prefix) ? relative : _options.Prefix.Trim('/') + "/" + relative;
        var metadata = new ObjectMetadata { ContentType = contentType };

        // 官方 SDK 为同步接口，放到线程池执行以免阻塞请求线程
        await Task.Run(() =>
        {
            using var stream = new MemoryStream(content, writable: false);
            _client.PutObject(_options.Bucket, objectKey, stream, metadata);
        }, cancellationToken);

        if (_options.PublicRead)
        {
            try
            {
                await Task.Run(() => _client.SetObjectAcl(_options.Bucket, objectKey, CannedAccessControlList.PublicRead), cancellationToken);
            }
            catch (Exception ex)
            {
                // Bucket 已为公共读、或使用 CDN 回源时无需对象级 ACL，失败不影响访问
                _logger.LogWarning(ex, "设置 OSS 对象公共读失败（Bucket 已公共读时可忽略）：{ObjectKey}", objectKey);
            }
        }

        return PublicUrl(objectKey);
    }

    public async Task<bool> DeleteAsync(string url, CancellationToken cancellationToken = default)
    {
        var objectKey = ToObjectKey(url);
        if (objectKey is null) return false;

        // 官方 SDK 为同步接口，放到线程池执行以免阻塞请求线程；对象不存在时 OSS 同样返回成功
        await Task.Run(() => _client.DeleteObject(_options.Bucket, objectKey), cancellationToken);
        return true;
    }

    /// <summary>把外链地址还原为对象键；未命中本存储的外链前缀时返回 null。</summary>
    private string? ToObjectKey(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var value = url.Trim();
        var baseUrl = PublicBase();
        if (!value.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase)) return null;
        var objectKey = value[baseUrl.Length..];
        return objectKey.Length == 0 || objectKey.Contains("..", StringComparison.Ordinal) ? null : objectKey;
    }

    /// <summary>外链前缀：优先自定义域名 / CDN，否则按 `&lt;bucket&gt;.&lt;endpoint&gt;` 推导。</summary>
    private string PublicBase()
    {
        if (!string.IsNullOrWhiteSpace(_options.PublicBaseUrl))
            return _options.PublicBaseUrl.TrimEnd('/') + "/";

        var endpoint = _options.Endpoint.Trim().TrimEnd('/');
        var scheme = "https";
        var separator = endpoint.IndexOf("://", StringComparison.Ordinal);
        if (separator > 0)
        {
            scheme = endpoint[..separator];
            endpoint = endpoint[(separator + 3)..];
        }
        return scheme + "://" + _options.Bucket + "." + endpoint + "/";
    }

    private string PublicUrl(string objectKey) => PublicBase() + objectKey;

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<string>>(() =>
        {
            var prefix = string.IsNullOrWhiteSpace(_options.Prefix) ? string.Empty : _options.Prefix.Trim('/') + "/";
            var urls = new List<string>();
            string? marker = null;
            do
            {
                var request = new ListObjectsRequest(_options.Bucket)
                {
                    Prefix = prefix,
                    Marker = marker,
                    MaxKeys = 200,
                };
                var result = _client.ListObjects(request);
                foreach (var summary in result.ObjectSummaries) urls.Add(PublicUrl(summary.Key));
                marker = result.IsTruncated ? result.NextMarker : null;
            } while (!string.IsNullOrEmpty(marker));

            return urls;
        }, cancellationToken);
}
