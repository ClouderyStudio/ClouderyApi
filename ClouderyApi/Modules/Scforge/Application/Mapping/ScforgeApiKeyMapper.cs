using ClouderyApi.Modules.Scforge.Api.Contracts;
using ClouderyApi.Modules.Scforge.Domain;

namespace ClouderyApi.Modules.Scforge.Application.Mapping;

/// <summary>
/// API Key 的对外投影。
///
/// 单独一个文件而不是塞进 <see cref="ScforgeMapper"/>：这套投影的**全部重点**是
/// 「绝不能把令牌明文漏出去」，与插件/版本的字段拼装不是一回事，物理隔离更不容易在改动时被带破。
/// </summary>
public static class ScforgeApiKeyMapper
{
    /// <summary>列表里展示的掩码：前缀 + 省略号 + 末尾 4 位。库里没有明文，这里只用到前缀。</summary>
    private const int TailVisible = 4;

    public static ScforgeApiKeyOut ToOut(ScforgeApiKey key)
    {
        var scopes = ScforgeApiKeyScopes.Parse(key.Scopes);
        var (usable, status) = ScforgeApiKeyAppService.Describe(key);

        return new ScforgeApiKeyOut
        {
            Id = key.Id.ToString(),
            Name = key.Name,
            Prefix = key.Prefix,
            MaskedToken = Mask(key.Prefix),
            Scopes = scopes,
            ScopeLabels = scopes
                .Select(s => ScforgeApiKeyScopes.Labels.GetValueOrDefault(s, s))
                .ToList(),
            UserId = key.UserId.ToString(),
            UserName = key.UserName,
            CreatedAt = ScforgeMapper.ToBeijing(key.CreatedAt),
            ExpiresAt = ScforgeMapper.ToBeijing(key.ExpiresAt),
            RevokedAt = ScforgeMapper.ToBeijing(key.RevokedAt),
            RevokedReason = key.RevokedReason,
            LastUsedAt = ScforgeMapper.ToBeijing(key.LastUsedAt),
            LastUsedIp = key.LastUsedIp,
            Status = status,
            Usable = usable,
        };
    }

    public static List<ScforgeApiKeyOut> ToOutList(IEnumerable<ScforgeApiKey> keys) =>
        keys.Select(ToOut).ToList();

    private static string Mask(string prefix) => prefix + "…" + new string('*', TailVisible);
}
