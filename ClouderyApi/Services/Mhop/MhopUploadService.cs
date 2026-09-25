using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Processing;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 图片上传（头像、帖子/回复图片）。对应 Python 后端的 routers/upload.py。
/// 使用 SixLabors.ImageSharp 做与 Pillow 版一致的处理：头像居中裁剪为 200x200 方图，
/// 帖子图片宽度超过 800 时等比缩放，统一编码为 WebP（质量 82）；
/// 编码结果交由 <see cref="IMhopObjectStorage"/> 写入本地磁盘或上传到远端阿里云 OSS。
/// </summary>
public sealed class MhopUploadService
{
    private const long MaxSize = 5 * 1024 * 1024;
    private const int ThumbMaxWidth = 800;
    private const int AvatarSize = 200;
    private const int WebpQuality = 82;
    private const long MaxPixels = 40_000_000; // 防解压炸弹

    private static readonly HashSet<string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/png", "image/webp", "image/gif",
    };

    private readonly IMhopObjectStorage _storage;
    private readonly ILogger<MhopUploadService> _logger;

    public MhopUploadService(IMhopObjectStorage storage, ILogger<MhopUploadService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>当前存储实现名称（local / aliyun-oss）。</summary>
    public string StorageProvider => _storage.Provider;

    public async Task<string> SaveAsync(IFormFile file, string subDirectory, CancellationToken cancellationToken = default)
    {
        if (file.Length > MaxSize) throw new MhopApiException(400, "图片大小不能超过 5MB");
        if (string.IsNullOrEmpty(file.ContentType) || !AllowedTypes.Contains(file.ContentType))
            throw new MhopApiException(400, "仅支持 JPG/PNG/WebP/GIF 格式");
        if (subDirectory is not ("avatars" or "posts")) throw new MhopApiException(400, "非法的上传类型");

        byte[] bytes;
        using (var memory = new MemoryStream())
        {
            await file.CopyToAsync(memory, cancellationToken);
            bytes = memory.ToArray();
        }
        if (bytes.Length > MaxSize) throw new MhopApiException(400, "图片大小不能超过 5MB");

        var square = subDirectory == "avatars";
        byte[] webp;

        try
        {
            using var image = Image.Load(bytes);
            if ((long)image.Width * image.Height > MaxPixels)
                throw new MhopApiException(400, "图片尺寸过大");

            if (square)
            {
                var side = Math.Min(image.Width, image.Height);
                var cropX = (image.Width - side) / 2;
                var cropY = (image.Height - side) / 2;
                image.Mutate(context => context
                    .Crop(new Rectangle(cropX, cropY, side, side))
                    .Resize(AvatarSize, AvatarSize));
            }
            else if (image.Width > ThumbMaxWidth)
            {
                var ratio = (double)ThumbMaxWidth / image.Width;
                var targetHeight = Math.Max(1, (int)(image.Height * ratio));
                image.Mutate(context => context.Resize(ThumbMaxWidth, targetHeight));
            }

            using var output = new MemoryStream();
            await image.SaveAsync(output, new WebpEncoder { Quality = WebpQuality }, cancellationToken);
            webp = output.ToArray();
        }
        catch (MhopApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "图片处理失败，拒绝上传");
            throw new MhopApiException(400, "不支持的图片格式");
        }

        var key = subDirectory + "/" + Guid.NewGuid().ToString("N") + ".webp";
        try
        {
            return await _storage.PutAsync(key, webp, "image/webp", cancellationToken);
        }
        catch (MhopApiException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "图片写入对象存储失败（{Provider}）：{Key}", _storage.Provider, key);
            throw new MhopApiException(502, "图片存储服务暂时不可用，请稍后重试");
        }
    }

    /// <summary>
    /// 尽力删除一批图片对象（帖子 / 回复被删除，或编辑时移除了图片）。
    /// 非本存储写入的地址直接跳过；单张失败只记日志，既不抛出也不影响主流程。
    /// </summary>
    public async Task DeleteAsync(IEnumerable<string> urls, CancellationToken cancellationToken = default)
    {
        var targets = urls.Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (targets.Count == 0) return;

        var deleted = 0;
        foreach (var url in targets)
        {
            try
            {
                if (await _storage.DeleteAsync(url, cancellationToken)) deleted++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "删除图片对象失败，已跳过：{Url}", url);
            }
        }

        _logger.LogInformation("图片清理完成：{Provider} 删除 {Deleted} / 请求 {Total} 张",
            _storage.Provider, deleted, targets.Count);
    }
}
