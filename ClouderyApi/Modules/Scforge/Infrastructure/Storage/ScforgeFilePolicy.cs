using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// 上传文件的校验、键名与图片转码规则。
///
/// 与存储位置无关：local 与 OSS 两种实现共用同一套白名单与命名，
/// 因此「同一个包在两种存储下的 StorageKey 与 sha256 完全一致」，切换存储不会造成语义漂移。
/// </summary>
internal static class ScforgeFilePolicy
{
    /// <summary>插件：生存战争的插件就是编译出来的 .dll 程序集。</summary>
    internal const string PluginExtension = ".dll";

    /// <summary>模组：生存战争的模组是 .netmod，会随服务器下发到客户端。</summary>
    internal const string ModExtension = ".netmod";

    internal static readonly HashSet<string> PackageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        PluginExtension, ModExtension,
    };

    /// <summary>是否为会下发到客户端的模组包。</summary>
    internal static bool IsMod(string? extension) =>
        string.Equals(extension, ModExtension, StringComparison.OrdinalIgnoreCase);

    /// <summary>历史上误当成插件包的压缩格式（生存战争并不使用）。</summary>
    private static readonly HashSet<string> RejectedArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".scpkg", ".rar", ".7z",
    };

    internal static readonly HashSet<string> ImageContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif",
    };

    private const int IconMaxEdge = 512;
    private const int IconWebpQuality = 88;
    private const long MaxPixels = 40_000_000;

    /// <summary>校验插件包并返回小写扩展名（同时限制大小与格式）。</summary>
    internal static string ValidatePackage(IFormFile file, long maxBytes)
    {
        if (file.Length <= 0) throw new Domain.ScforgeRuleException("插件包为空，请选择有效文件");
        if (file.Length > maxBytes) throw new Domain.ScforgeRuleException($"插件包不能超过 {maxBytes / 1024 / 1024} MB");

        var extension = Path.GetExtension(Path.GetFileName(file.FileName));
        if (!string.IsNullOrEmpty(extension) && RejectedArchiveExtensions.Contains(extension))
        {
            throw new Domain.ScforgeRuleException("生存战争不支持压缩包形式的插件：插件请上传 .dll，模组请上传 .netmod");
        }

        if (string.IsNullOrEmpty(extension) || !PackageExtensions.Contains(extension))
        {
            throw new Domain.ScforgeRuleException("仅支持 .dll（插件）与 .netmod（模组）两种格式");
        }

        return extension.ToLowerInvariant();
    }

    /// <summary>读取上传流为字节数组，并复核实际长度（不信任 Content-Length）。</summary>
    internal static async Task<byte[]> ReadBytesAsync(IFormFile file, long maxBytes, CancellationToken cancellationToken)
    {
        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        if (bytes.Length == 0) throw new Domain.ScforgeRuleException("插件包为空，请选择有效文件");
        if (bytes.Length > maxBytes) throw new Domain.ScforgeRuleException($"插件包不能超过 {maxBytes / 1024 / 1024} MB");
        return bytes;
    }

    /// <summary>插件包的内容寻址键：packages/{sha256}{ext}。</summary>
    internal static string PackageKey(byte[] bytes, string extension) =>
        $"packages/{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))}{extension}";

    internal static string Sha256(byte[] bytes) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    /// <summary>内容寻址的存储键：由键本身就能判断是否重复上传。</summary>
    internal static string ExtensionFromKey(string storageKey) => Path.GetExtension(storageKey);

    /// <summary>校验图片并解码重编码为 WebP，返回字节。</summary>
    internal static byte[] EncodeImage(IFormFile file, long maxBytes, ILogger logger)
    {
        if (file.Length <= 0) throw new Domain.ScforgeRuleException("图片为空，请选择有效文件");
        if (file.Length > maxBytes) throw new Domain.ScforgeRuleException($"图片不能超过 {maxBytes / 1024 / 1024} MB");
        if (string.IsNullOrEmpty(file.ContentType) || !ImageContentTypes.Contains(file.ContentType))
        {
            throw new Domain.ScforgeRuleException("仅支持 JPG / PNG / WebP / GIF 格式的图片");
        }

        using var upload = new MemoryStream();
        file.CopyTo(upload);
        var bytes = upload.ToArray();
        if (bytes.Length == 0) throw new Domain.ScforgeRuleException("图片为空，请选择有效文件");
        if (bytes.Length > maxBytes) throw new Domain.ScforgeRuleException($"图片不能超过 {maxBytes / 1024 / 1024} MB");

        try
        {
            using var image = Image.Load(bytes);
            if ((long)image.Width * image.Height > MaxPixels) throw new Domain.ScforgeRuleException("图片尺寸过大");
            if (image.Width > IconMaxEdge || image.Height > IconMaxEdge)
            {
                image.Mutate(context => context.Resize(new ResizeOptions
                {
                    Size = new Size(IconMaxEdge, IconMaxEdge),
                    Mode = ResizeMode.Max,
                }));
            }

            using var output = new MemoryStream();
            image.Save(output, new WebpEncoder { Quality = IconWebpQuality });
            return output.ToArray();
        }
        catch (Domain.ScforgeRuleException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "图片处理失败，拒绝上传");
            throw new Domain.ScforgeRuleException("不支持的图片格式");
        }
    }

    /// <summary>图片文件名（不含目录）。</summary>
    internal static string ImageFileName() => $"{Guid.NewGuid():N}.webp";
}
