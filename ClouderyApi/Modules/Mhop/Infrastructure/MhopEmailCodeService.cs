using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Modules.Mhop.Infrastructure;

/// <summary>
/// 邮箱登录验证码服务（从 Python 后端 email_code.py 迁移）。
/// 验证码存内存（单进程部署足够；多进程/多实例请换 Redis）：
/// 6 位数字、10 分钟有效、校验成功即失效（一次性）、最多错 5 次；
/// 同一邮箱 60 秒发送间隔；同一 IP 每小时最多 20 次发送。
/// Smtp:Host 未配置时为开发模式：不真正发信，验证码写日志并返回给调用方。
/// </summary>
public sealed class MhopEmailCodeService
{
    public const int CodeTtlSeconds = 600;
    public const int ResendIntervalSeconds = 60;
    private const int MaxAttempts = 5;
    private const int IpHourlyLimit = 20;
    /// <summary>同时跟踪的验证码邮箱数上限；超过即拒绝新发码（防随机邮箱刷量撑爆内存）。</summary>
    private const int MaxTrackedEmails = 20_000;
    /// <summary>同时跟踪的 IP 数上限；超过即拒绝新发码（防伪造 / 轮换 IP 撑爆内存）。</summary>
    private const int MaxTrackedIps = 20_000;

    private readonly record struct CodeEntry(string Code, long ExpiresAt, int Attempts);

    private readonly MhopOptions _options;
    private readonly MhopSmtpClient _smtp;
    private readonly ILogger<MhopEmailCodeService> _logger;
    private readonly object _lock = new();

    private readonly ConcurrentDictionary<string, CodeEntry> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> _lastSent = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<long>> _ipWindow = new(StringComparer.Ordinal);

    public MhopEmailCodeService(IOptions<MhopOptions> options, MhopSmtpClient smtp, ILogger<MhopEmailCodeService> logger)
    {
        _options = options.Value;
        _smtp = smtp;
        _logger = logger;
    }

    public bool SmtpConfigured => !string.IsNullOrWhiteSpace(_options.Smtp.Host);

    public static bool ValidEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > 254) return false;
        var at = email.IndexOf('@');
        if (at <= 0 || at == email.Length - 1) return false;
        var local = email[..at];
        var domain = email[(at + 1)..];
        if (local.Length == 0 || domain.Length == 0) return false;
        if (local.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '_' or '%' or '+' or '-'))) return false;
        var dot = domain.LastIndexOf('.');
        if (dot <= 0 || dot == domain.Length - 1) return false;
        if (domain[..dot].Any(c => !(char.IsLetterOrDigit(c) || c is '-' or '.'))) return false;
        return domain[(dot + 1)..].All(char.IsLetter);
    }

    /// <summary>同一 IP 每小时发送上限。返回 false 表示超限。</summary>
    public bool CheckIpRate(string ip)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            // IP 维度同样做容量保护：ClientIp 在未配可信代理时可能拿到大量不同取值，
            // 不设上限则 _ipWindow 会随伪造 / 轮换 IP 无界增长。
            if (!_ipWindow.TryGetValue(ip, out var window))
            {
                if (_ipWindow.Count >= MaxTrackedIps)
                {
                    foreach (var (tracked, timestamps) in _ipWindow)
                    {
                        timestamps.RemoveAll(timestamp => now - timestamp >= 3600);
                        if (timestamps.Count == 0) _ipWindow.TryRemove(tracked, out _);
                    }
                    // 清理后仍无空间（新 IP 全都是新鲜的）：本次直接拒绝，等老窗口自然过期。
                    if (_ipWindow.Count >= MaxTrackedIps)
                    {
                        _logger.LogWarning("验证码 IP 频控缓存已达上限 {Max}，本次请求被拒绝", MaxTrackedIps);
                        return false;
                    }
                }
                window = [];
                _ipWindow[ip] = window;
            }

            window.RemoveAll(timestamp => now - timestamp >= 3600);
            if (window.Count >= IpHourlyLimit) return false;
            window.Add(now);
            return true;
        }
    }

    /// <summary>
    /// 生成验证码。返回 (code, retryAfter)；频控中时 code 为 null。
    /// </summary>
    public (string? Code, int RetryAfter) IssueCode(string email)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            var wait = ResendIntervalSeconds - (now - _lastSent.GetValueOrDefault(email, 0L));
            if (wait > 0) return (null, (int)wait + 1);

            // 容量保护：IssueCode 不校验邮箱是否存在（那是防枚举的有意设计），
            // 因此匿名请求可用随机邮箱把字典撑到无界增长；这里在写入前清理过期项，
            // 仍超上限则拒绝发码——宁可让极少数正常用户晚点收信，也不能被撑爆内存。
            if (_codes.Count >= MaxTrackedEmails)
            {
                PruneExpiredLocked(now);
                if (_codes.Count >= MaxTrackedEmails)
                {
                    _logger.LogWarning("验证码缓存已达上限 {Max}，本次发码请求被拒绝（可能存在随机邮箱刷量）", MaxTrackedEmails);
                    return (null, ResendIntervalSeconds);
                }
            }

            var code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6");
            _codes[email] = new CodeEntry(code, now + CodeTtlSeconds, 0);
            _lastSent[email] = now;
            return (code, 0);
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
            if (now - sentAt > ResendIntervalSeconds) _lastSent.TryRemove(email, out _);
        }
    }

    /// <summary>校验并一次性消费验证码。</summary>
    public bool VerifyCode(string email, string code)
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        lock (_lock)
        {
            if (!_codes.TryGetValue(email, out var entry)) return false;
            if (now > entry.ExpiresAt || entry.Attempts >= MaxAttempts)
            {
                _codes.TryRemove(email, out _);
                return false;
            }
            if (!FixedTimeEquals(entry.Code, (code ?? string.Empty).Trim()))
            {
                _codes[email] = entry with { Attempts = entry.Attempts + 1 };
                return false;
            }
            _codes.TryRemove(email, out _);
            return true;
        }
    }

    /// <summary>发送成功返回 true；开发模式（未配置 SMTP）返回 false，验证码见日志。</summary>
    public async Task<bool> DeliverAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        if (!SmtpConfigured)
        {
            _logger.LogWarning(
                "[开发模式] 邮箱验证码登录：{Email} 的验证码为 {Code}（配置 SMTP 后将真正发邮件）", email, code);
            return false;
        }

        var appName = _options.AppName;
        var subject = $"【{appName}】登录验证码 {code}";
        var text =
            $"你正在登录{appName}。\n\n" +
            $"你的登录验证码是：{code}\n" +
            "验证码 10 分钟内有效，请勿泄露给他人。如非本人操作，请忽略本邮件。\n\n" +
            $"—— {appName}";
        var html =
            "<div style=\"max-width:480px;margin:0 auto;font-family:'Microsoft YaHei',Arial,sans-serif\">" +
            $"<h2 style=\"color:#2f8f83;margin-bottom:8px\">{appName}</h2>" +
            "<p style=\"color:#555\">你正在登录，本次验证码为：</p>" +
            $"<div style=\"margin:20px 0;font-size:34px;font-weight:700;letter-spacing:8px;color:#2f8f83\">{code}</div>" +
            "<p style=\"color:#888;font-size:13px\">验证码 10 分钟内有效，请勿泄露给他人。如非本人操作，请忽略本邮件。</p>" +
            "<hr style=\"border:none;border-top:1px solid #eee;margin:24px 0\">" +
            "<p style=\"color:#aaa;font-size:12px\">本邮件由系统自动发送，请勿回复。如遇紧急心理困扰，请拨打全国心理援助热线 12356。</p>" +
            "</div>";

        await _smtp.SendAsync(email, subject, text, html, cancellationToken);
        return true;
    }

    private static bool FixedTimeEquals(string left, string right)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
}
