using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ClouderyApi.Tests.TestSupport;

/// <summary>
/// 把宿主日志（含异常堆栈）留一份在内存里，断言语义上只关心「500」时仍能看到真实原因：
/// ApiErrorBodyMiddleware 把未处理异常统一吞成 { detail: "服务器内部错误" }，异常只落在日志里。
/// </summary>
public sealed class RecordingLoggerProvider : ILoggerProvider
{
    private readonly ConcurrentQueue<string> _entries = new();

    /// <summary>日志级别 &gt;= Warning 的全部条目（含异常堆栈），按写入顺序。</summary>
    public string Text => string.Join("\n", _entries);

    public ILogger CreateLogger(string categoryName) => new RecordingLogger(categoryName, _entries);

    public void Dispose()
    {
    }

    private sealed class RecordingLogger(string category, ConcurrentQueue<string> entries) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning)
            {
                return;
            }

            var line = $"[{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                line += $"\n{exception}";
            }

            entries.Enqueue(line);
        }
    }
}
