using System.Security.Cryptography;
using System.Text;
using Casdoor.Client;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// Casdoor 统一身份认证（OAuth2 授权码 + OIDC）适配层：复用项目根 Casdoor 配置，
/// 但登录成功后签发的是 MHOP 自己的 JWT（与用户名密码登录返回同一 TokenOut），
/// 因此 MHOP 前端无需改动既有令牌存取逻辑。
/// state 采用 HMAC 签名 + 过期时间的无状态实现，避免 SPA 跨域 Cookie 问题。
/// </summary>
public sealed class MhopCasdoorService
{
    private const int StateTtlSeconds = 600;

    private readonly IConfiguration _configuration;
    private readonly MhopOptions _options;
    private readonly IHttpClientFactory _httpClientFactory;

    public MhopCasdoorService(
        IConfiguration configuration, IOptions<MhopOptions> options, IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _options = options.Value;
        _httpClientFactory = httpClientFactory;
    }

    public string? Endpoint => _configuration["Casdoor:Endpoint"];

    public string? OrganizationName => _configuration["Casdoor:OrganizationName"];

    public string? ApplicationName => _configuration["Casdoor:ApplicationName"];

    public string? ClientId => _configuration["Casdoor:ClientId"];

    public string Scope => _configuration.GetSection("Casdoor:Scopes").Get<string[]>() is { Length: > 0 } scopes
        ? string.Join(' ', scopes)
        : "openid profile email";

    public int StateTtl => StateTtlSeconds;

    /// <summary>配置齐全且未被显式关闭时，统一身份登录可用。</summary>
    public bool Enabled =>
        _options.Casdoor.Enabled
        && !string.IsNullOrWhiteSpace(Endpoint)
        && !string.IsNullOrWhiteSpace(ClientId)
        && !string.IsNullOrWhiteSpace(_configuration["Casdoor:ClientSecret"]);

    /// <summary>
    /// 计算前端回调地址：优先取 Mhop:Casdoor:RedirectUri；否则取白名单内的 Origin + 固定 SPA 回调路径；
    /// 最后退回 API 自身地址。该地址需在 Casdoor 应用的 Redirect URIs 中登记。
    /// </summary>
    public string ResolveRedirectUri(HttpRequest request)
    {
        if (!string.IsNullOrWhiteSpace(_options.Casdoor.RedirectUri))
            return _options.Casdoor.RedirectUri.Trim();

        var origin = request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin) && IsAllowedOrigin(origin, request))
            return origin.TrimEnd('/') + "/auth/casdoor/callback";

        return $"{request.Scheme}://{request.Host.Value}/auth/casdoor/callback";
    }

    public string CreateState()
    {
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var expiresAt = DateTimeOffset.UtcNow.AddSeconds(StateTtlSeconds).ToUnixTimeSeconds();
        var payload = $"{nonce}.{expiresAt}";
        return payload + "." + Convert.ToHexString(Sign(payload)).ToLowerInvariant();
    }

    public bool ValidateState(string? state)
    {
        if (string.IsNullOrWhiteSpace(state)) return false;
        var parts = state.Split('.');
        if (parts.Length != 3) return false;
        if (!long.TryParse(parts[1], out var expiresAt)) return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expiresAt) return false;

        byte[] provided;
        try
        {
            provided = Convert.FromHexString(parts[2]);
        }
        catch
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(Sign($"{parts[0]}.{parts[1]}"), provided);
    }

    /// <summary>用授权码换取令牌并解析 OIDC 用户信息（Casdoor 的 JWT 中已含 id/name/email/avatar）。</summary>
    public async Task<CasdoorUser?> ExchangeAsync(string code, string redirectUri)
    {
#pragma warning disable CS8601 // 引用类型赋值可能为 null（来自配置）
        var casdoorOptions = new CasdoorOptions
        {
            Endpoint = Endpoint,
            OrganizationName = OrganizationName,
            ApplicationName = ApplicationName,
            ApplicationType = _configuration["Casdoor:ApplicationType"],
            ClientId = ClientId,
            ClientSecret = _configuration["Casdoor:ClientSecret"],
            CallbackPath = _configuration["Casdoor:CallbackPath"],
        };
#pragma warning restore CS8601

        var client = new CasdoorClient(_httpClientFactory.CreateClient("Casdoor"), casdoorOptions);
        var tokenResponse = await client.RequestAuthorizationCodeTokenAsync(code, redirectUri);
        if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken)) return null;

        client.SetBearerToken(tokenResponse.AccessToken);
        return client.ParseJwtToken(tokenResponse.AccessToken, false);
    }

    private byte[] Sign(string payload)
        => HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.Jwt.Secret), Encoding.UTF8.GetBytes(payload));

    /// <summary>仅放行 CORS 白名单内的来源，或与 API 同主机的来源（避免任意来源指定回调地址）。</summary>
    private bool IsAllowedOrigin(string origin, HttpRequest request)
    {
        var normalized = origin.TrimEnd('/');
        var allowed = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (allowed.Any(item => string.Equals(item.TrimEnd('/'), normalized, StringComparison.OrdinalIgnoreCase)))
            return true;

        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
               && string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase);
    }
}
