using Aliyun.OSS;
using Aliyun.OSS.Common;
using ClouderyApi.Modules.Scforge.Application;
using ClouderyApi.Modules.Scforge.Domain;

namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// 阿里云 OSS 实现（官方 SDK，OSS 自有签名协议）。
///
/// 对象布局（同一 Bucket 内靠 <c>Scforge:Storage:Oss:Prefix</c> 与其它业务隔离）：
///   <c>{prefix}/packages/{sha256}{ext}</c>  插件包：**私有**对象，不设 ACL，仍由 API 流式下发；
///   <c>{prefix}/images/{guid}.webp</c>      图片：公共读，返回 PublicBaseUrl 外链，浏览器直连。
///
/// 插件包保持私有是有意的：下载入口必须唯一（计数与「未过审不可下载」都在那里判定），
/// 直接给出预签名外链会把这两件事绕过去。
/// </summary>
public sealed class OssScforgeFileStore : IScforgeFileStore
{
    private const string PackageContentType = "application/octet-stream";

    private readonly OssClient _client;
    private readonly ScforgeOptions _options;
    private readonly ScforgeOssOptions _oss;
    private readonly ILogger<OssScforgeFileStore> _logger;

    public OssScforgeFileStore(ScforgeOptions options, ILogger<OssScforgeFileStore> logger)
    {
        _options = options;
        _oss = options.Storage.Oss;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(_oss.Endpoint)
            || string.IsNullOrWhiteSpace(_oss.Bucket)
            || string.IsNullOrWhiteSpace(_oss.AccessKeyId)
            || string.IsNullOrWhiteSpace(_oss.AccessKeySecret))
        {
            throw new InvalidOperationException(
                "Scforge:Storage:Provider=oss 需要在 Scforge:Storage:Oss 中配置 Endpoint / Bucket / AccessKeyId / AccessKeySecret。");
        }

        _client = string.IsNullOrWhiteSpace(_oss.SecurityToken)
            ? new OssClient(_oss.Endpoint, _oss.AccessKeyId, _oss.AccessKeySecret)
            : new OssClient(_oss.Endpoint, _oss.AccessKeyId, _oss.AccessKeySecret, _oss.SecurityToken);
    }

    public string Provider => "aliyun-oss";

    public bool IsRemote => true;

    public async Task<ScforgeStoredPackage> SavePackageAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var extension = ScforgeFilePolicy.ValidatePackage(file, _options.MaxPackageBytes);
        var bytes = await ScforgeFilePolicy.ReadBytesAsync(file, _options.MaxPackageBytes, cancellationToken);
        var key = ScforgeFilePolicy.PackageKey(bytes, extension);
        var objectKey = ObjectKey(key);
        var fileName = Path.GetFileName(file.FileName);

        using var probe = new MemoryStream(bytes, writable: false);
        // 先校验再上传：坏包不该占用远端存储。
        ScforgePackageInspector.EnsureReadable(probe, extension);
        var manifest = ScforgePackageInspector.ReadManifest(probe, extension, _logger);

        // 内容寻址：同一份包重复上传时直接复用已有对象。
        var exists = await Task.Run(() => _client.DoesObjectExist(_oss.Bucket, objectKey), cancellationToken);
        if (!exists)
        {
            var metadata = new ObjectMetadata
            {
                ContentType = PackageContentType,
                ContentDisposition = AttachmentHeader(fileName),
            };
            await Task.Run(() =>
            {
                using var stream = new MemoryStream(bytes, writable: false);
                _client.PutObject(_oss.Bucket, objectKey, stream, metadata);
            }, cancellationToken);
        }

        return new ScforgeStoredPackage(key, fileName, bytes.Length, ScforgeFilePolicy.Sha256(bytes), extension, manifest);
    }

    public async Task<string> SaveImageAsync(IFormFile file, CancellationToken cancellationToken = default)
    {
        var webp = ScforgeFilePolicy.EncodeImage(file, _options.MaxImageBytes, _logger);
        var objectKey = ObjectKey($"images/{ScforgeFilePolicy.ImageFileName()}");

        var metadata = new ObjectMetadata { ContentType = "image/webp" };
        await Task.Run(() =>
        {
            using var stream = new MemoryStream(webp, writable: false);
            _client.PutObject(_oss.Bucket, objectKey, stream, metadata);
        }, cancellationToken);

        if (_oss.PublicRead)
        {
            try
            {
                await Task.Run(() => _client.SetObjectAcl(_oss.Bucket, objectKey, CannedAccessControlList.PublicRead), cancellationToken);
            }
            catch (Exception ex)
            {
                // Bucket 已为公共读、或使用 CDN 回源时无需对象级 ACL，失败不影响访问。
                _logger.LogWarning(ex, "设置 OSS 对象公共读失败（Bucket 已公共读时可忽略）：{ObjectKey}", objectKey);
            }
        }

        return PublicUrl(objectKey);
    }

    public async Task<ScforgeOpenedPackage?> OpenPackageAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var objectKey = ObjectKey(storageKey);
        try
        {
            var result = await Task.Run(() => _client.GetObject(_oss.Bucket, objectKey), cancellationToken);
            var length = result.Metadata.ContentLength > 0
                ? result.Metadata.ContentLength
                : result.ContentLength;
            // OssObject 持有底层响应流，必须随流一起释放，否则连接不会归还连接池。
            return new ScforgeOpenedPackage(new OwnedStream(result.Content, result), length);
        }
        catch (OssException ex) when (ex.ErrorCode == "NoSuchKey")
        {
            return null;
        }
    }

    public void TryDelete(string? storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey)) return;
        try
        {
            _client.DeleteObject(_oss.Bucket, ObjectKey(storageKey));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "删除 OSS 对象失败，已跳过：{Key}", storageKey);
        }
    }

    public void TryDeleteByUrl(string? publicUrl)
    {
        if (string.IsNullOrWhiteSpace(publicUrl)) return;
        var baseUrl = PublicBase();
        if (!publicUrl.StartsWith(baseUrl, StringComparison.OrdinalIgnoreCase)) return;

        var objectKey = publicUrl[baseUrl.Length..];
        if (objectKey.Length == 0 || objectKey.Contains("..", StringComparison.Ordinal)) return;

        try
        {
            _client.DeleteObject(_oss.Bucket, objectKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "按 URL 删除 OSS 对象失败，已跳过：{Url}", publicUrl);
        }
    }

    /// <summary>对象键 = 前缀 + 相对键。</summary>
    private string ObjectKey(string storageKey)
    {
        var relative = ScforgeUploadPaths.NormalizeKey(storageKey);
        var prefix = (_oss.Prefix ?? string.Empty).Trim('/');
        return prefix.Length == 0 ? relative : prefix + "/" + relative;
    }

    /// <summary>外链前缀：优先自定义域名 / CDN，否则按 &lt;bucket&gt;.&lt;endpoint&gt; 推导。</summary>
    private string PublicBase()
    {
        if (!string.IsNullOrWhiteSpace(_oss.PublicBaseUrl)) return _oss.PublicBaseUrl.TrimEnd('/') + "/";

        var endpoint = _oss.Endpoint.Trim().TrimEnd('/');
        var scheme = "https";
        var separator = endpoint.IndexOf("://", StringComparison.Ordinal);
        if (separator > 0)
        {
            scheme = endpoint[..separator];
            endpoint = endpoint[(separator + 3)..];
        }

        return scheme + "://" + _oss.Bucket + "." + endpoint + "/";
    }

    private string PublicUrl(string objectKey) => PublicBase() + objectKey;

    /// <summary>RFC 5987 的下载名；纯 ASCII 时用简写形式。</summary>
    private static string AttachmentHeader(string fileName)
    {
        var safe = string.IsNullOrWhiteSpace(fileName) ? "package.dll" : fileName;
        return safe.All(char.IsAscii) && !safe.Contains('"')
            ? $"attachment; filename=\"{safe}\""
            : "attachment; filename*=UTF-8''" + Uri.EscapeDataString(safe);
    }

    /// <summary>把底层流与它的属主（OssObject）绑在一起释放。</summary>
    private sealed class OwnedStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            inner.ReadAsync(buffer, offset, count, cancellationToken);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
                owner.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
