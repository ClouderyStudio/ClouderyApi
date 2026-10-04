namespace ClouderyApi.Modules.Scforge.Infrastructure;

/// <summary>
/// SCForge 模块配置（appsettings.json 的 <c>Scforge</c> 节）。
/// </summary>
public sealed class ScforgeOptions
{
    public const string SectionName = "Scforge";

    /// <summary>上传根目录；相对路径按内容根解析，留空则用 <c>scforge-uploads</c>。</summary>
    public string? UploadDir { get; set; }

    /// <summary>单个插件包大小上限（字节），默认 64 MB。</summary>
    public long MaxPackageBytes { get; set; } = 64L * 1024 * 1024;

    /// <summary>单张图片大小上限（字节），默认 4 MB。</summary>
    public long MaxImageBytes { get; set; } = 4L * 1024 * 1024;

    /// <summary>列表接口单页最大条数，默认 60。</summary>
    public int MaxPageSize { get; set; } = 60;

    /// <summary>
    /// 插件包与图片的存放位置：local=本机磁盘（插件包私有目录 + /scforge/uploads 静态托管图片）；
    /// oss=阿里云 OSS（图片公共读并返回外链，插件包保持私有、仍经下载接口流出）。
    /// </summary>
    public ScforgeStorageOptions Storage { get; set; } = new();
}

/// <summary>存储方式配置（Scforge:Storage）。</summary>
public sealed class ScforgeStorageOptions
{
    /// <summary>local（默认）或 oss（别名 aliyun）。</summary>
    public string Provider { get; set; } = "local";

    public ScforgeOssOptions Oss { get; set; } = new();
}

/// <summary>阿里云 OSS 配置（Scforge:Storage:Oss）。</summary>
public sealed class ScforgeOssOptions
{
    /// <summary>Endpoint，如 oss-cn-hangzhou.aliyuncs.com，可带 https:// 前缀。</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string AccessKeySecret { get; set; } = string.Empty;

    /// <summary>STS 临时凭证的 SecurityToken；使用长期密钥时留空。</summary>
    public string SecurityToken { get; set; } = string.Empty;

    /// <summary>对外访问域名（CDN 或 Bucket 自定义域名）。留空则按 Bucket + Endpoint 推导。</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>图片上传后设为公共读（插件包永远不设，保持私有）。</summary>
    public bool PublicRead { get; set; } = true;

    /// <summary>对象键前缀，便于与同一 Bucket 内其它业务隔离。插件包与图片分别在其下分 packages / images。</summary>
    public string Prefix { get; set; } = "scforge";
}
