using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Services.Mhop;

public interface IMhopJwtService
{
    string CreateToken(int userId, string role);

    MhopTokenPayload? ValidateToken(string token);
}

public sealed record MhopTokenPayload(int UserId, string Role);

/// <summary>
/// HS256 JWT 签发与校验（与 Python 后端的 PyJWT 令牌结构一致：sub / role / exp / iat）。
/// 手写实现避免引入额外 NuGet 依赖，且只接受 HS256，使用固定时间比较防时序攻击。
/// </summary>
public sealed class MhopJwtService : IMhopJwtService
{
    private readonly MhopOptions _options;

    public MhopJwtService(IOptions<MhopOptions> options) => _options = options.Value;

    public string CreateToken(int userId, string role)
    {
        var now = DateTimeOffset.UtcNow;
        var header = EncodeSegment(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = EncodeSegment(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["sub"] = userId.ToString(),
            ["role"] = role,
            ["exp"] = now.AddHours(_options.Jwt.ExpireHours).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
        }));
        var signature = EncodeSegment(Sign($"{header}.{payload}"));
        return $"{header}.{payload}.{signature}";
    }

    public MhopTokenPayload? ValidateToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var parts = token.Split('.');
        if (parts.Length != 3) return null;

        byte[] actualSignature;
        byte[] headerBytes;
        byte[] payloadBytes;
        try
        {
            headerBytes = DecodeSegment(parts[0]);
            payloadBytes = DecodeSegment(parts[1]);
            actualSignature = DecodeSegment(parts[2]);
        }
        catch
        {
            return null;
        }

        using (var headerDoc = JsonDocument.Parse(headerBytes))
        {
            if (!headerDoc.RootElement.TryGetProperty("alg", out var alg) || alg.GetString() != "HS256")
                return null;
        }

        var expectedSignature = Sign($"{parts[0]}.{parts[1]}");
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature)) return null;

        try
        {
            using var payloadDoc = JsonDocument.Parse(payloadBytes);
            var root = payloadDoc.RootElement;
            if (!root.TryGetProperty("exp", out var exp) || !exp.TryGetInt64(out var expiresAt)) return null;
            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= expiresAt) return null;
            if (!root.TryGetProperty("sub", out var sub) || !int.TryParse(sub.GetString(), out var userId)) return null;
            var role = root.TryGetProperty("role", out var roleEl) ? roleEl.GetString() ?? "user" : "user";
            return new MhopTokenPayload(userId, role);
        }
        catch
        {
            return null;
        }
    }

    private byte[] Sign(string signingInput)
        => HMACSHA256.HashData(Encoding.UTF8.GetBytes(_options.Jwt.Secret), Encoding.UTF8.GetBytes(signingInput));

    private static string EncodeSegment(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] DecodeSegment(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            0 => padded,
            _ => throw new FormatException("非法 Base64Url 片段"),
        };
        return Convert.FromBase64String(padded);
    }
}
