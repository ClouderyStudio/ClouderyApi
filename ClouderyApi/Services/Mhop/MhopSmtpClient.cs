using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using Microsoft.Extensions.Options;

namespace ClouderyApi.Services.Mhop;

/// <summary>
/// 极简 SMTP 客户端：同时支持隐式 SSL(465) 与 STARTTLS(587) 以及 AUTH LOGIN。
/// .NET 内置 SmtpClient 不支持隐式 SSL，而腾讯/网易邮箱常用 465，故这里手写协议实现，
/// 也避免引入 MailKit 等额外依赖。
/// </summary>
public sealed class MhopSmtpClient
{
    private readonly MhopOptions _options;
    private readonly ILogger<MhopSmtpClient> _logger;

    public MhopSmtpClient(IOptions<MhopOptions> options, ILogger<MhopSmtpClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task SendAsync(
        string to, string subject, string textBody, string htmlBody, CancellationToken cancellationToken = default)
    {
        var smtp = _options.Smtp;
        var from = string.IsNullOrWhiteSpace(smtp.From) ? smtp.User : smtp.From;

        using var client = new TcpClient();
        await client.ConnectAsync(smtp.Host, smtp.Port, cancellationToken);

        Stream stream = client.GetStream();
        StreamReader? reader = null;
        StreamWriter? writer = null;
        try
        {
            if (smtp.UseSsl) stream = await UpgradeToTlsAsync(stream, smtp.Host, cancellationToken);
            (reader, writer) = CreateIo(stream);

            await ExpectAsync(reader, "220", cancellationToken);
            var ehlo = await CommandAsync(writer, reader, "EHLO mhop.local", cancellationToken);

            if (!smtp.UseSsl)
            {
                EnsureCode(ehlo, "220");
                var startTls = await CommandAsync(writer, reader, "STARTTLS", cancellationToken);
                EnsureCode(startTls, "220");
                stream = await UpgradeToTlsAsync(stream, smtp.Host, cancellationToken);
                reader.Dispose();
                writer.Dispose();
                (reader, writer) = CreateIo(stream);
                await CommandAsync(writer, reader, "EHLO mhop.local", cancellationToken);
            }

            if (!string.IsNullOrEmpty(smtp.User))
            {
                var auth = await CommandAsync(writer, reader, "AUTH LOGIN", cancellationToken);
                EnsureCode(auth, "334");
                auth = await CommandAsync(
                    writer, reader, Convert.ToBase64String(Encoding.UTF8.GetBytes(smtp.User)), cancellationToken);
                EnsureCode(auth, "334");
                auth = await CommandAsync(
                    writer, reader, Convert.ToBase64String(Encoding.UTF8.GetBytes(smtp.Password)), cancellationToken);
                EnsureCode(auth, "235");
            }

            EnsureCode(await CommandAsync(writer, reader, $"MAIL FROM:<{from}>", cancellationToken), "250");
            EnsureCode(await CommandAsync(writer, reader, $"RCPT TO:<{to}>", cancellationToken), "250");
            EnsureCode(await CommandAsync(writer, reader, "DATA", cancellationToken), "354");

            var message = BuildMessage(from, to, subject, textBody, htmlBody);
            await writer.WriteAsync(message + ".\r\n");
            await writer.FlushAsync(cancellationToken);
            EnsureCode(await ReadResponseAsync(reader, cancellationToken), "250");

            await CommandAsync(writer, reader, "QUIT", cancellationToken);
            _logger.LogInformation("登录验证码邮件已发送至 {Email}", to);
        }
        finally
        {
            writer?.Dispose();
            reader?.Dispose();
            stream.Dispose();
        }
    }

    private static (StreamReader Reader, StreamWriter Writer) CreateIo(Stream stream)
    {
        var reader = new StreamReader(stream, new UTF8Encoding(false), false, 1024, leaveOpen: true);
        var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true)
        {
            AutoFlush = true,
            NewLine = "\r\n",
        };
        return (reader, writer);
    }

    private static async Task<Stream> UpgradeToTlsAsync(Stream stream, string host, CancellationToken cancellationToken)
    {
        var ssl = new SslStream(stream, leaveInnerStreamOpen: false, (_, _, _, _) => true);
        await ssl.AuthenticateAsClientAsync(
            new SslClientAuthenticationOptions { TargetHost = host, EnabledSslProtocols = SslProtocols.None },
            cancellationToken);
        return ssl;
    }

    private static async Task<string> CommandAsync(
        StreamWriter writer, StreamReader reader, string command, CancellationToken cancellationToken)
    {
        await writer.WriteAsync(command + "\r\n");
        await writer.FlushAsync(cancellationToken);
        return await ReadResponseAsync(reader, cancellationToken);
    }

    private static async Task<string> ReadResponseAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) throw new IOException("SMTP 连接被服务端关闭");
            lines.Add(line);
            // 多行响应以 "250-" 续行、以 "250 " 结束
            if (line.Length < 4 || line[3] != '-') break;
        }
        return string.Join("\n", lines);
    }

    private static async Task ExpectAsync(StreamReader reader, string code, CancellationToken cancellationToken)
        => EnsureCode(await ReadResponseAsync(reader, cancellationToken), code);

    private static void EnsureCode(string response, string expectedCode)
    {
        if (!response.StartsWith(expectedCode, StringComparison.Ordinal))
            throw new IOException($"SMTP 响应异常，期望 {expectedCode}，实际：{response}");
    }

    private static string BuildMessage(string from, string to, string subject, string textBody, string htmlBody)
    {
        var boundary = "mhop_" + Guid.NewGuid().ToString("N");
        var builder = new StringBuilder();
        builder.Append("From: ").Append(from).Append("\r\n");
        builder.Append("To: ").Append(to).Append("\r\n");
        builder.Append("Subject: ").Append(EncodeHeader(subject)).Append("\r\n");
        builder.Append("MIME-Version: 1.0\r\n");
        builder.Append("Content-Type: multipart/alternative; boundary=\"").Append(boundary).Append("\"\r\n");
        builder.Append("\r\n");
        AppendPart(builder, boundary, "text/plain", textBody);
        AppendPart(builder, boundary, "text/html", htmlBody);
        builder.Append("--").Append(boundary).Append("--\r\n");
        return builder.ToString();
    }

    private static void AppendPart(StringBuilder builder, string boundary, string contentType, string body)
    {
        builder.Append("--").Append(boundary).Append("\r\n");
        builder.Append("Content-Type: ").Append(contentType).Append("; charset=utf-8\r\n");
        // base64 编码：避免中文 SMTP 传输问题，且内容不含行首 '.' 无需 dot-stuffing
        builder.Append("Content-Transfer-Encoding: base64\r\n\r\n");
        builder.Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(body))).Append("\r\n");
    }

    private static string EncodeHeader(string value)
    {
        if (value.All(c => c <= 0x7F)) return value;
        return "=?UTF-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)) + "?=";
    }
}
