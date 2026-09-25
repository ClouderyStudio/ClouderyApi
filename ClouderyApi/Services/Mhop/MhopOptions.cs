namespace ClouderyApi.Services.Mhop;

/// <summary>
/// MHOP 模块配置（appsettings.json 的 Mhop 节）。对应 Python 后端的 pydantic-settings Settings。
/// </summary>
public sealed class MhopOptions
{
    public const string SectionName = "Mhop";

    public string AppName { get; set; } = "MHOP 公益心理辅助平台";

    public MhopJwtOptions Jwt { get; set; } = new();

    public MhopLlmOptions Llm { get; set; } = new();

    public MhopSmtpOptions Smtp { get; set; } = new();

    public MhopCasdoorOptions Casdoor { get; set; } = new();

    /// <summary>合作机构（北京乐科心理干预研究院）专属援助电话；留空则不展示。</summary>
    public string LekeHotline { get; set; } = string.Empty;

    /// <summary>本地存储根目录（头像、帖子图片）。相对路径以内容根目录为基准；使用远端 OSS 时仅用于兼容历史文件。</summary>
    public string UploadDir { get; set; } = "uploads";

    /// <summary>图片存储方式：本地磁盘或远端阿里云 OSS。</summary>
    public MhopStorageOptions Storage { get; set; } = new();
}

/// <summary>图片存储配置（Mhop:Storage）：local=本机磁盘并由 /mhop/uploads 静态托管；oss=上传到远端阿里云 OSS。</summary>
public sealed class MhopStorageOptions
{
    /// <summary>local（默认）或 oss（别名 aliyun）。</summary>
    public string Provider { get; set; } = "local";

    public MhopOssOptions Oss { get; set; } = new();
}

/// <summary>阿里云 OSS 配置（Mhop:Storage:Oss）。</summary>
public sealed class MhopOssOptions
{
    /// <summary>Endpoint，如 oss-cn-hangzhou.aliyuncs.com，可带 https:// 前缀。</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string AccessKeyId { get; set; } = string.Empty;

    public string AccessKeySecret { get; set; } = string.Empty;

    /// <summary>STS 临时凭证的 SecurityToken；使用主账号 / 子账号长期密钥时留空。</summary>
    public string SecurityToken { get; set; } = string.Empty;

    /// <summary>对外访问域名（CDN 或 Bucket 自定义域名），如 https://cdn.example.com。留空则按 Bucket + Endpoint 推导。</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>上传后把对象设为公共读；Bucket 已公共读或走 CDN 回源时可关闭。</summary>
    public bool PublicRead { get; set; } = true;

    /// <summary>对象键前缀，便于与同一 Bucket 内其它业务隔离。</summary>
    public string Prefix { get; set; } = "mhop";
}

public sealed class MhopJwtOptions
{
    public string Secret { get; set; } = "dev-only-change-me-in-production";

    public int ExpireHours { get; set; } = 72;
}

/// <summary>OpenAI 兼容 Chat Completions 配置；留空时自动降级为内置共情式规则回复。</summary>
public sealed class MhopLlmOptions
{
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "glm-4-flash";
}

/// <summary>Casdoor 统一身份认证（复用项目根 Casdoor 配置，这里只放 MHOP 侧开关与回调地址）。</summary>
public sealed class MhopCasdoorOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>SPA 回调地址；留空则按请求 Origin（须在 Cors 白名单内）自动推导。</summary>
    public string RedirectUri { get; set; } = string.Empty;
}

/// <summary>
/// SMTP 邮箱发信（邮箱验证码登录用）。Host 留空 = 开发模式：验证码不真正发送，
/// 写在后端日志里，且发送接口临时返回 dev_code 供本机联调。
/// </summary>
public sealed class MhopSmtpOptions
{
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 465;

    public string User { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>留空则取 User。</summary>
    public string From { get; set; } = string.Empty;

    /// <summary>true=隐式 SSL(465)；false=STARTTLS(587)。</summary>
    public bool UseSsl { get; set; } = true;
}
