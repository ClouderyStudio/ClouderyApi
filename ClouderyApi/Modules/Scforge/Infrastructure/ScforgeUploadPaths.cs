namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>上传目录与公开访问路径的统一解析。</summary>
public static class ScforgeUploadPaths
{
    /// <summary>静态托管前缀（Program.cs 的 UseStaticFiles RequestPath）。</summary>
    public const string RequestPath = "/scforge/uploads";

    /// <summary>默认上传目录名。</summary>
    public const string DefaultDirectoryName = "scforge-uploads";

    /// <summary>插件包目录（私有，只经下载接口流出）。</summary>
    public const string PackagesDirectory = "packages";

    /// <summary>本地存储里图片的目录（位于被静态托管的 public/ 之下）。</summary>
    public const string PublicImagesDirectory = "public/images";

    /// <summary>对外图片 URL 的相对段（/scforge/uploads 之后的部分）。</summary>
    public const string ImagesSegment = "images";

    /// <summary>解析上传根目录：相对路径按内容根展开。</summary>
    public static string ResolveLocalRoot(string? configured, string contentRoot)
    {
        var value = string.IsNullOrWhiteSpace(configured) ? DefaultDirectoryName : configured.Trim();
        return Path.IsPathRooted(value) ? value : Path.Combine(contentRoot, value);
    }

    /// <summary>规范化对象键：统一分隔符、去掉首部斜杠、拒绝路径穿越。</summary>
    public static string NormalizeKey(string key)
    {
        var normalized = (key ?? string.Empty).Replace('\\', '/').TrimStart('/');
        if (normalized.Length == 0 || normalized.Contains("..", StringComparison.Ordinal))
        {
            throw new Domain.ScforgeRuleException("非法的文件名");
        }

        return normalized;
    }

    /// <summary>本地存储里图片的公开 URL（/scforge/uploads/images/xxx）。</summary>
    public static string PublicImageUrl(string fileName) => $"{RequestPath}/{ImagesSegment}/{fileName}";
}
