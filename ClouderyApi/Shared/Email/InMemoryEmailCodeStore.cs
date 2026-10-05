using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace ClouderyApi.Shared.Email;

/// <summary>
/// 进程内验证码存储（未配置 Redis 时的默认实现）。
/// 单进程部署足够；多进程 / 多实例请配置 Redis 换成共享实现。
/// </summary>
public sealed class InMemoryEmailCodeStore : IEmailCodeStore
{
    private readonly record struct CodeEntry(string Code, long ExpiresAt, int Attempts);

    private readonly ILogger<InMemoryEmailCodeStore> _logger;
    private readonly object _lock = new();

    private readonly ConcurrentDictionary<string, CodeEntry> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _lastSent = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<long>> _ipWindow = new(StringComparer.Ordinal);

    public InMemoryEmailCodeStore(ILogger<InMemoryEmailCodeStore> logger) => _logger = logger;

    public ValueTask<bool> CheckIpRateAsync(string ip)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            // IP 维度同样做容量保护：ClientIp 在未配可信代理时可能拿到大量不同取值，
            // 不设上限则 _ipWindow 会随伪造 / 轮换 IP 无界增长。
            if (!_ipWindow.TryGetValue(ip, out var window))
            {
                if (_ipWindow.Count >= EmailCodeDefaults.MaxTrackedIps)
                {
                    foreach (var (tracked, timestamps) in _ipWindow)
                    {
                        timestamps.RemoveAll(timestamp => now - timestamp >= EmailCodeDefaults.IpWindowSeconds);
                        if (timestamps.Count == 0) _ipWindow.TryRemove(tracked, out _);
                    }
                    // 清理后仍无空间（新 IP 全都是新鲜的）：本次直接拒绝，等老窗口自然过期。
                    if (_ipWindow.Count >= EmailCodeDefaults.MaxTrackedIps)
                    {
                        _logger.LogWarning("验证码 IP 频控缓存已达上限 {Max}，本次请求被拒绝", EmailCodeDefaults.MaxTrackedIps);
                        return ValueTask.FromResult(false);
                    }
                }
                window = [];
                _ipWindow[ip] = window;
            }

            window.RemoveAll(timestamp => now - timestamp >= EmailCodeDefaults.IpWindowSeconds);
            if (window.Count >= EmailCodeDefaults.IpHourlyLimit) return ValueTask.FromResult(false);
            window.Add(now);
            return ValueTask.FromResult(true);
        }
    }

    public ValueTask<EmailCodeIssueResult> IssueCodeAsync(string email)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            var wait = EmailCodeDefaults.ResendIntervalSeconds - (now - _lastSent.GetValueOrDefault(email, 0L));
            if (wait > 0) return ValueTask.FromResult(new EmailCodeIssueResult(null, (int)wait + 1));

            // 容量保护：IssueCode 不校验邮箱是否存在（那是防枚举的有意设计），
            // 因此匿名请求可用随机邮箱把字典撑到无界增长；这里在写入前清理过期项，
            // 仍超上限则拒绝发码——宁可让极少数正常用户晚点收信，也不能被撑爆内存。
            if (_codes.Count >= EmailCodeDefaults.MaxTrackedEmails)
            {
                PruneExpiredLocked(now);
                if (_codes.Count >= EmailCodeDefaults.MaxTrackedEmails)
                {
                    _logger.LogWarning(
                        "验证码缓存已达上限 {Max}，本次发码请求被拒绝（可能存在随机邮箱刷量）",
                        EmailCodeDefaults.MaxTrackedEmails);
                    return ValueTask.FromResult(new EmailCodeIssueResult(null, EmailCodeDefaults.ResendIntervalSeconds));
                }
            }

            var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
            _codes[email] = new CodeEntry(code, now + EmailCodeDefaults.CodeTtlSeconds, 0);
            _lastSent[email] = now;
            return ValueTask.FromResult(new EmailCodeIssueResult(code, 0));
        }
    }

    public ValueTask<bool> VerifyCodeAsync(string email, string code)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            if (!_codes.TryGetValue(email, out var entry)) return ValueTask.FromResult(false);
            if (now > entry.ExpiresAt || entry.Attempts >= EmailCodeDefaults.MaxAttempts)
            {
                _codes.TryRemove(email, out _);
                return ValueTask.FromResult(false);
            }
            if (!FixedTimeEquals(entry.Code, (code ?? string.Empty).Trim()))
            {
                _codes[email] = entry with { Attempts = entry.Attempts + 1 };
                return ValueTask.FromResult(false);
            }
            _codes.TryRemove(email, out _);
            return ValueTask.FromResult(true);
        }
    }

    /// <summary>清理已过期的验证码与发送记录（调用方须持有 _lock）。</summary>
    private void PruneExpiredLocked(long now)
    {
        foreach (var (email, entry) in _codes)
        {
            if (now > entry.ExpiresAt) _codes.TryRemove(email, out _);
        }

        // _lastSent 只用于「同一邮箱 60 秒内不重复发」；验证码一旦删除，
        // 该记录也没有意义了（它只可能比 _codes 多留几十秒）。
        foreach (var (email, sentAt) in _lastSent)
        {
            if (now - sentAt > EmailCodeDefaults.ResendIntervalSeconds) _lastSent.TryRemove(email, out _);
        }
    }

    private static bool FixedTimeEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
