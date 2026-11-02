using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using SolapiSender.Core.Security;

namespace SolapiSender.Core.Logging;

/// <summary>
/// Ghi log ra %LOCALAPPDATA%\SolapiSender\logs\app-yyyyMMdd.log, giữ <see cref="RetentionDays"/> ngày.
/// Không bao giờ ghi header Authorization hay API secret (các client không log chúng).
/// </summary>
public sealed class FileLoggerProvider : ILoggerProvider
{
    public const int RetentionDays = 14;

    private readonly string _directory;
    private readonly LogLevel _minLevel;
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<string, FileLogger> _loggers = new();

    public FileLoggerProvider(string? directory = null, LogLevel minLevel = LogLevel.Information)
    {
        _directory = directory ?? AppPaths.LogDirectory;
        _minLevel = minLevel;
        Directory.CreateDirectory(_directory);
        CleanupOldFiles();
    }

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new FileLogger(this, name));

    public void Dispose() => _loggers.Clear();

    private void Write(string category, LogLevel level, string message, Exception? ex)
    {
        var now = DateTime.Now;
        var sb = new StringBuilder()
            .Append(now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(Short(level)).Append("] ")
            .Append(category[(category.LastIndexOf('.') + 1)..]).Append(": ")
            .AppendLine(message);
        if (ex is not null) sb.AppendLine(ex.ToString());

        var file = Path.Combine(_directory, $"app-{now:yyyyMMdd}.log");
        lock (_lock)
        {
            try { File.AppendAllText(file, sb.ToString(), Encoding.UTF8); }
            catch (IOException) { /* không để lỗi ghi log làm sập app */ }
        }
    }

    private void CleanupOldFiles()
    {
        try
        {
            foreach (var f in Directory.EnumerateFiles(_directory, "app-*.log"))
                if (File.GetLastWriteTime(f) < DateTime.Now.AddDays(-RetentionDays)) File.Delete(f);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static string Short(LogLevel l) => l switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "---",
    };

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= provider._minLevel && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            provider.Write(category, logLevel, formatter(state, exception), exception);
        }
    }
}
