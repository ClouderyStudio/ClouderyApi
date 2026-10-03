using System.Net;
using Microsoft.AspNetCore.Http;

namespace ClouderyApi.Shared.Filters;

/// <summary>
/// 统一的客户端 IP 取值口径，供全局限流、<see cref="IpRateLimitAttribute"/> 与验证码频控共用。
/// <para>
/// 直接读 <c>Connection.RemoteIpAddress</c> 在生产（应用位于 Nginx / 云负载均衡之后）拿到的是
/// 代理 IP，于是所有请求共用同一个计数桶：正常用户互相挤掉额度，攻击者却不受影响。
/// 配好 <c>ForwardedHeaders</c> 可信代理后，中间件已把 RemoteIpAddress 改写为真实客户端 IP，
/// 这里直接读它即可，无需自己解析 X-Forwarded-For——自己解析等于无认证地信任客户端提供的头。
/// </para>
/// </summary>
public static class ClientIp
{
    /// <summary>
    /// 取用于限流与频控的客户端 IP。IPv6 映射的 IPv4 地址（::ffff:10.0.0.1）归一化为 IPv4，
    /// 否则同一个客户端在双栈代理下会被算成两个额度。
    /// </summary>
    public static string Resolve(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        if (address is null) return "unknown";

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        else if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
                 && address.IsIPv6LinkLocal)
        {
            // IPv6 链路本地地址不可唯一标识客户端，归一到 loopback 桶，避免每个请求换 key。
            address = IPAddress.Loopback;
        }

        return address.ToString();
    }
}