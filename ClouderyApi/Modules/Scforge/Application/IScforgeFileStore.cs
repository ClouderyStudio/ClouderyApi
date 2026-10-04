using Microsoft.AspNetCore.Http;

namespace ClouderyApi.Modules.Scforge.Application;

/// <summary>
/// 一次成功落盘的插件包（内容寻址）。
/// <paramref name="Manifest"/> 由存储实现在落盘时顺带解析：省掉「再读一次远端对象」的往返。
/// </summary>
public sealed record ScforgeStoredPackage(
    string StorageKey,
    string FileName,
    long Size,
    string Sha256,
    string Extension,
    ScforgeManifest? Manifest);

/// <summary>从插件包读出的 manifest.json（字段全部可选）。</summary>
public sealed record ScforgeManifest(string? Name, string? Version, string? Author, string? Main);

/// <summary>已打开的插件包：流 + 长度（远端对象的大小来自元数据）。</summary>
public sealed record ScforgeOpenedPackage(Stream Stream, long Length);

/// <summary>
/// SCForge 的文件边界：应用服务只依赖本接口。
///
/// 两个实现由 <c>Scforge:Storage:Provider</c> 选择：
///   • local —— 本机磁盘（<c>packages/</c> 私有目录 + <c>public/images/</c> 静态托管）；
///   • oss  —— 阿里云 OSS（图片公共读并返回外链，插件包保持私有，仍由 API 流式下发）。
/// </summary>
public interface IScforgeFileStore
{
    /// <summary>实现名称（local / aliyun-oss），用于启动日志与排障。</summary>
    string Provider { get; }

    /// <summary>是否为远端对象存储。</summary>
    bool IsRemote { get; }

    /// <summary>保存插件包（校验扩展名、大小与包体魔数，按 sha256 内容寻址）。</summary>
    Task<ScforgeStoredPackage> SavePackageAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>保存图片（解码后统一转 WebP 并缩放），返回可直接下发的公开 URL。</summary>
    Task<string> SaveImageAsync(IFormFile file, CancellationToken cancellationToken = default);

    /// <summary>打开一个插件包用于下载；对象缺失时返回 null。</summary>
    Task<ScforgeOpenedPackage?> OpenPackageAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>尽力删除一个对象，失败只记日志。</summary>
    void TryDelete(string? storageKey);

    /// <summary>按公开 URL 删除图片（非本存储写入的地址会被忽略）。</summary>
    void TryDeleteByUrl(string? publicUrl);
}
