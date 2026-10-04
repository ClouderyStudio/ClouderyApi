using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ClouderyApi.Modules.Scforge.Domain;

/// <summary>
/// API Key 的作用域集合（值对象）。
///
/// 沿用 <see cref="ScforgePermissionSet"/> 的形态：JSON 数组列存、解析时丢弃未知码。
/// 这里的码与管理员权限码**刻意分开** —— 后台能审稿不代表该把机器凭据也发出去，
/// 因此 <see cref="Manage"/> 单独一档、且只能由超管在后台签发，不能自助申请。
/// </summary>
public static class ScforgeApiKeyScopes
{
    /// <summary>
    /// 令牌固定前缀。放在 Domain 层而不是中间件里，是为了让签发方（Application）
    /// 与校验方（Infrastructure）引用同一个常量，不必互相依赖对方的命名空间。
    /// 带上它便于在日志与流量里一眼认出这类凭据。
    /// </summary>
    public const string TokenPrefix = "scf_";

    /// <summary>发布插件与追加版本（<c>POST /scforge/plugins</c>、<c>POST .../versions</c>）。</summary>
    public const string Publish = "publish";

    /// <summary>读取自己的资源列表（<c>GET /scforge/plugins/mine</c> 等）。</summary>
    public const string Read = "read";

    /// <summary>编辑与删除自己的插件、重提审核。</summary>
    public const string Manage = "manage";

    /// <summary>全部可授予作用域（顺序即界面展示顺序）。</summary>
    public static readonly IReadOnlyList<string> All = [Read, Publish, Manage];

    /// <summary>可自助申请的作用域（不含 <see cref="Manage"/>）。</summary>
    public static readonly IReadOnlyList<string> SelfService = [Read, Publish];

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [Read] = "读取",
        [Publish] = "发布",
        [Manage] = "管理",
    };

    public static readonly IReadOnlyDictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        [Read] = "查询自己发布的插件与版本列表",
        [Publish] = "发布新插件、追加新版本（走与网页相同的审核流程）",
        [Manage] = "编辑与删除自己的插件、把被驳回的提交重新送审",
    };

    public static bool IsValid(string? code) => All.Contains(code, StringComparer.Ordinal);

    /// <summary>丢弃未知码并去重；<c>null</c> 视作空集合。</summary>
    public static IReadOnlyList<string> From(IEnumerable<string>? codes) =>
        codes is null ? [] : codes.Where(All.Contains).Distinct(StringComparer.Ordinal).ToList();

    public static string Serialize(IEnumerable<string>? codes) => JsonSerializer.Serialize(From(codes));

    /// <summary>解析存储的作用域 JSON；空白 / 非法 JSON / null 一律返回空集合（等于没有任何权限）。</summary>
    public static IReadOnlyList<string> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return From(JsonSerializer.Deserialize<List<string>>(json));
        }
        catch
        {
            return [];
        }
    }

    /// <summary>把令牌明文转成入库用的哈希。base64url，避免哈希里出现 + / = 这类需要转义的字符。</summary>
    public static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
