using System.Net;

namespace ClouderyApi.Shared.Options;

/// <summary>
/// 可信反向代理配置（appsettings.json 的 <c>TrustedProxies</c> 节）。
/// <para>
/// 生产部署在 Nginx / 云负载均衡之后，此时 <c>HttpContext.Connection.RemoteIpAddress</c>
/// 拿到的是代理 IP，于是按 IP 的限流（全局 300/分钟、IpRateLimitAttribute、邮箱验证码频控）
/// 会退化成「全站共用一个计数桶」：既挡不住换出口 IP 的攻击，又会误伤正常用户。
/// </para>
/// <para>
/// 显式配置可信代理后，应用按 <c>X-Forwarded-For</c> 取真实客户端 IP。
/// <b>必须只填自己控制的代理地址</b>——若信任任意来源，攻击者可以自己伪造该头，
/// 每次请求换一个 IP，限流将被完全绕过。两项都留空时表示「不信任任何代理」，
/// 沿用RemoteIpAddress 的原行为（本地开发与直连部署都属这一档）。
/// </para>
/// </summary>
public sealed class TrustedProxyOptions
{
    public const string SectionName = "TrustedProxies";

    /// <summary>可信代理的 IP 地址（精确匹配），如 127.0.0.1。</summary>
    public string[] Proxies { get; set; } = [];

    /// <summary>
    /// 可信代理的 IP 网段（CIDR），如 172.16.0.0/12、10.0.0.0/8。
    /// 容器网络常用网段整体作为可信代理。
    /// </summary>
    public string[] Networks { get; set; } = [];

    /// <summary>是否启用。默认仅当 <see cref="Proxies"/> 或 <see cref="Networks"/> 非空时自动启用。</summary>
    public bool? Enabled { get; set; }

    /// <summary>是否信任 X-Forwarded-For（多跳代理时需开启；单层 Nginx 一般不需要）。</summary>
    public bool TrustForwardedFor { get; set; } = true;

    /// <summary>是否信任 X-Forwarded-Proto（影响 UseHttpsRedirection 与生成绝对 URL）。</summary>
    public bool TrustForwardedProto { get; set; } = true;

    /// <summary>是否信任 X-Forwarded-Host。</summary>
    public bool TrustForwardedHost { get; set; }

    /// <summary>解析出可信代理配置；无任何可信来源时返回 false。</summary>
    public bool TryResolve(out IPAddress[] proxies, out IPNetwork[] networks)
    {
        proxies = (Proxies ?? [])
            .Select(value => IPAddress.TryParse(value?.Trim(), out var address) ? address : null)
            .Where(address => address is not null)
            .Select(address => address!)
            .ToArray();

        networks = (Networks ?? [])
            .Select(ParseNetwork)
            .OfType<IPNetwork>()
            .ToArray();

        var explicitlyEnabled = Enabled ?? true;
        return explicitlyEnabled && (proxies.Length > 0 || networks.Length > 0);
    }

    /// <summary>解析 CIDR；缺省前缀长度按地址族取全长度（即精确匹配该单个地址）。</summary>
    private static IPNetwork? ParseNetwork(string? cidr)
    {
        if (string.IsNullOrWhiteSpace(cidr)) return null;
        var parts = cidr.Trim().Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var address)) return null;

        var fullLength = address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 128 : 32;
        var prefixLength = parts.Length == 2 && int.TryParse(parts[1], out var parsed) ? parsed : fullLength;
        prefixLength = Math.Clamp(prefixLength, 0, fullLength);

        try
        {
            return new IPNetwork(address, prefixLength);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}