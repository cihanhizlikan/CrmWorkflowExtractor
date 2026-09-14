using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Crm.Cli.Logging;

/// <summary>
/// Writes every entry to <c>logs/run.log</c> and warnings and above to <c>logs/warnings.txt</c> (§8). Hand-rolled:
/// two append-only files do not justify a logging framework. Disposed before the run is sealed, so the manifest
/// hashes the complete logs.
/// </summary>
public sealed class RunLogProvider : ILoggerProvider
{
    private readonly Lock _gate = new();
    private readonly StreamWriter _runLog;
    private readonly StreamWriter _warnings;

    public RunLogProvider(string runLogPath, string warningsPath)
    {
        UTF8Encoding utf8 = new(encoderShouldEmitUTF8Identifier: false);
        _runLog = new StreamWriter(runLogPath, append: true, utf8) { NewLine = "\n", AutoFlush = true };
        _warnings = new StreamWriter(warningsPath, append: true, utf8) { NewLine = "\n", AutoFlush = true };
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new RunLogger(this, categoryName);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _runLog.Dispose();
            _warnings.Dispose();
        }
    }

    private void Write(LogLevel level, string category, string message, Exception? exception)
    {
        string stamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        string line = $"{stamp} [{Abbreviation(level)}] {category}: {message}";
        if (exception is not null)
        {
            line += "\n" + exception;
        }
        lock (_gate)
        {
            _runLog.WriteLine(line);
            if (level >= LogLevel.Warning)
            {
                _warnings.WriteLine(line);
            }
        }
    }

    private static string Abbreviation(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "---"
        };
    }

    private sealed class RunLogger(RunLogProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel >= LogLevel.Debug;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                provider.Write(logLevel, category, formatter(state, exception), exception);
            }
        }
    }
}
