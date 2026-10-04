using ClouderyApi.Modules.Scforge.Application;
using System.IO.Compression;
using System.Text.Json;

namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// 插件包自省与包体格式校验。
///
/// 生存战争只有两种包：
///   • 插件 <c>.dll</c> —— 编译出来的 .NET 程序集，服务端加载；
///   • 模组 <c>.netmod</c> —— **会随服务器下发到客户端**，同样可以写插件逻辑。
///   （没有 .zip 形式的插件或模组；zip 只可能出现在 .netmod 内部。）
///
/// 校验按**魔数**判断，而不是只看后缀，避免「把压缩包改成 .dll 上传」这类混淆：
///   • .dll 必须以 PE 头的 <c>MZ</c> 开头；
///   • .netmod 若以 zip 魔数 <c>PK\x03\x04</c> 开头，就按 zip 严格校验并可读取其中的 manifest.json；
///     否则视为不透明的容器格式，原样接受。
///
/// 全程面向 <see cref="Stream"/>：本地磁盘与 OSS 两种存储共用同一套解析逻辑。
/// </summary>
public static class ScforgePackageInspector
{
    private static readonly byte[] PeMagic = [(byte)'M', (byte)'Z'];
    private static readonly byte[] ZipMagic = [0x50, 0x4B, 0x03, 0x04];

    /// <summary>校验包体确实是它所声称的格式；不合格直接拒绝，避免发布之后才发现是坏文件。</summary>
    public static void EnsureReadable(Stream stream, string extension)
    {
        if (IsDll(extension))
        {
            var head = Peek(stream, 2);
            if (!head.SequenceEqual(PeMagic))
            {
                throw new Domain.ScforgeRuleException("这不是有效的 .dll 程序集（缺少 PE 头），请上传编译产物而不是改了后缀的文件");
            }

            return;
        }

        // 只有看起来是 zip 的 .netmod 才按 zip 校验；其余容器格式原样放行。
        if (LooksLikeZip(stream))
        {
            try
            {
                using var archive = new ZipArchive(Rewound(stream), ZipArchiveMode.Read, leaveOpen: true);
                _ = archive.Entries.Count;
            }
            catch (Exception)
            {
                throw new Domain.ScforgeRuleException("这个 .netmod 是损坏的压缩包，请重新打包");
            }
        }
    }

    /// <summary>读取清单；没有清单（.dll，或不是 zip 的 .netmod）时返回 null。</summary>
    public static ScforgeManifest? ReadManifest(Stream stream, string extension, ILogger logger)
    {
        if (!LooksLikeZip(stream)) return null;

        try
        {
            using var archive = new ZipArchive(Rewound(stream), ZipArchiveMode.Read, leaveOpen: true);
            var entry = FindManifestEntry(archive);
            if (entry is null) return null;

            using var entryStream = entry.Open();
            using var document = JsonDocument.Parse(entryStream, new JsonDocumentOptions { AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            return new ScforgeManifest(
                ReadString(root, "name"),
                ReadString(root, "version"),
                ReadString(root, "author"),
                ReadString(root, "main"));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "包内 manifest.json 解析失败，按无清单处理");
            return null;
        }
    }

    private static bool IsDll(string extension) =>
        extension.Equals(".dll", StringComparison.OrdinalIgnoreCase);

    /// <summary>流的前几个字节是否等于 zip 魔数（不改变流位置）。</summary>
    private static bool LooksLikeZip(Stream stream)
    {
        var head = Peek(stream, 4);
        return head.Length == 4 && head.SequenceEqual(ZipMagic);
    }

    /// <summary>在不移动流位置的前提下读取前 n 个字节。</summary>
    private static byte[] Peek(Stream stream, int count)
    {
        if (!stream.CanSeek) return [];
        var origin = stream.Position;
        try
        {
            var buffer = new byte[count];
            var read = stream.Read(buffer, 0, count);
            return read == count ? buffer : buffer[..read];
        }
        finally
        {
            stream.Position = origin;
        }
    }

    private static Stream Rewound(Stream stream)
    {
        if (stream.CanSeek) stream.Position = 0;
        return stream;
    }

    /// <summary>只看根目录或唯一一层子目录下的 manifest.json，避免被深层的同名文件误导。</summary>
    private static ZipArchiveEntry? FindManifestEntry(ZipArchive archive)
    {
        var candidates = archive.Entries
            .Where(e => e.Name.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            .Where(e => e.FullName.Count(c => c == '/') <= 1)
            .OrderBy(e => e.FullName.Count(c => c == '/'))
            .ThenBy(e => e.FullName.Length)
            .ToList();

        return candidates.Count == 0 ? null : candidates[0];
    }

    private static string? ReadString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value)) return null;
        if (value.ValueKind != JsonValueKind.String) return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
